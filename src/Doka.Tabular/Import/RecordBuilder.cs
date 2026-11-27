namespace Doka.Tabular;

/// <summary>Builds the records of one source row from node plans and their typed slots.</summary>
[RequiresDynamicCode("Records are created by compiled creators.")]
[RequiresUnreferencedCode("Records are created by creators that bind constructors by reflection.")]
internal static class RecordBuilder
{
    public static bool TryBuild(
        NodePlan plan,
        SourceRecord record,
        RowContext row,
        ConversionContext context,
        out object? value
    )
    {
        value = null;
        Array.Clear(plan.Failed);

        return ReadMembers(plan, record, row, context)
            && CheckRules(plan, row)
            && Array.IndexOf(plan.Failed, true) < 0
            && TryCreate(plan, row, out value);
    }

    /// <summary>Reads every member into its slot; <see langword="false" /> when a fatal error ends the row.</summary>
    private static bool ReadMembers(
        NodePlan plan,
        SourceRecord record,
        RowContext row,
        ConversionContext context
    )
    {
        var members = plan.Members;
        for (var index = 0; index < members.Length; index++)
        {
            if (row.Fatal is not null)
            {
                return false;
            }

            var member = members[index];
            var read = member switch
            {
                { Reader: { } reader } => reader
                    .Read(record, row, context, plan.Node.Model, member.Slot),
                { Parts: { } parts } => ReadComposed(plan, member, parts, record, row, context),
                { Nested: { } nested } => ReadNested(member, nested, record, row, context),
                _ => true,
            };

            plan.Failed[index] = !read;
        }

        return row.Fatal is null;
    }

    private static bool ReadComposed(
        NodePlan plan,
        MemberPlan member,
        IReadOnlyList<FieldReader> parts,
        SourceRecord record,
        RowContext row,
        ConversionContext context
    )
    {
        var partSlots = member.PartSlots!;
        var partsOk = true;
        for (var index = 0; index < parts.Count; index++)
        {
            partsOk &= parts[index]
                .Read(record, row, context, plan.Node.Model, partSlots[index]);
        }

        if (!partsOk)
        {
            return false;
        }

        var composed = (ComposeMember)member.Member;
        try
        {
            composed.Binding.Read(partSlots, member.Slot);
        }
        catch (Exception exception) when (Callbacks.Failed(exception, row.CallCancellation))
        {
            Reject(
                exception,
                plan.Models,
                row,
                PathOf(plan, member.Member),
                "The composed value was rejected.",
                $"The read function of {member.Member.Name} threw an unexpected exception.");

            return false;
        }

        if (composed.IsNullable
            || !member.Slot.HoldsNull)
        {
            return true;
        }

        row.Fatal ??= TabularError.Create(
            TabularErrorCodes.Callback.InvalidResult,
            TabularErrorKind.Technical,
            $"The read function of {member.Member.Name} returned null for a property that is not nullable.",
            row.Location(path: PathOf(plan, member.Member)));

        return false;
    }

    /// <summary>Builds a nested record; a nullable one whose fields are all empty is <see langword="null" />.</summary>
    private static bool ReadNested(
        MemberPlan member,
        NodePlan nested,
        SourceRecord record,
        RowContext row,
        ConversionContext context
    )
    {
        var kind = member.Member.Kind;
        if (((NestedMember)member.Member).IsNullable
            && IsEmptySubstructure(nested, record, context))
        {
            kind.Assign(member.Slot, null);

            return true;
        }

        if (!TryBuild(nested, record, row, context, out var inner))
        {
            return false;
        }

        kind.Assign(member.Slot, inner);

        return true;
    }

    /// <summary>
    /// Checks the rules over several fields; <see langword="false" /> when a rule threw. Every rule whose fields
    /// converted and passed their field rules runs; a violation marks its fields only after all rules ran, so one
    /// rule does not hide another.
    /// </summary>
    private static bool CheckRules(
        NodePlan plan,
        RowContext row
    )
    {
        // WHY: Maps have at most a few rules; the violated ones are noted without an allocation per record.
        var violated = plan.Rules.Length <= 32
            ? stackalloc int[plan.Rules.Length]
            : new int[plan.Rules.Length];

        var count = 0;
        foreach (var (rule, positions, inputs) in plan.Rules)
        {
            if (positions is null
                || AnyFailed(plan.Failed, positions))
            {
                continue;
            }

            bool valid;
            try
            {
                valid = rule.Rule(inputs);
            }
            catch (Exception exception) when (Callbacks.Failed(exception, row.CallCancellation))
            {
                row.Fatal ??= TabularError.Create(
                    TabularErrorCodes.Callback.Failed,
                    TabularErrorKind.Technical,
                    "A rule over several fields threw an exception.",
                    row.Location(path: plan.Node.Path));

                return false;
            }

            if (!valid)
            {
                violated[count++] = positions[0];
                row.Errors.Add(
                    TabularError.Create(
                        rule.Code ?? TabularErrorCodes.Validation.Failed,
                        TabularErrorKind.Data,
                        "The values violate a rule over several fields.",
                        row.Location(path: PathOf(plan, rule.Members[0]))));
            }
        }

        foreach (var position in violated[..count])
        {
            plan.Failed[position] = true;
        }

        return true;
    }

    private static bool AnyFailed(
        bool[] failed,
        int[] positions
    )
    {
        // WHY: Runs per record; a query would capture the failure flags in a closure.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var position in positions)
        {
            if (failed[position])
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCreate(
        NodePlan plan,
        RowContext row,
        out object? value
    )
    {
        var model = plan.Node.Model;
        try
        {
            value = model.Factory is { } factory
                ? factory(plan.Values)
                : plan.Creator!(plan.Slots);
        }
        catch (Exception exception) when (Callbacks.Failed(exception, row.CallCancellation))
        {
            value = null;
            Reject(
                exception,
                plan.Models,
                row,
                plan.Node.Path,
                "The record was rejected by its constructor or factory.",
                model.Factory is not null
                    ? $"The factory of {model.Type.Name} threw an unexpected exception."
                    : $"The constructor of {model.Type.Name} threw an unexpected exception.");

            return false;
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        // WHY: A factory of the application can return null despite its annotation; that is no rejection of the
        // record but an invalid callback result.
        if (value is null)
        {
            row.Fatal ??= TabularError.Create(
                TabularErrorCodes.Callback.InvalidResult,
                TabularErrorKind.Technical,
                $"The factory of {model.Type.Name} returned null.",
                row.Location(path: plan.Node.Path));

            return false;
        }

        return true;
    }

    private static void Reject(
        Exception exception,
        MapModel[] models,
        RowContext row,
        TabularPropertyPath? path,
        string dataMessage,
        string technicalMessage
    )
    {
        foreach (var model in models)
        {
            if (model.FindExpectation(exception) is { } code)
            {
                row.Errors.Add(TabularError.Create(code, TabularErrorKind.Data, dataMessage, row.Location(path: path)));

                return;
            }
        }

        row.Fatal ??= TabularError.Create(
            TabularErrorCodes.Callback.Failed,
            TabularErrorKind.Technical,
            technicalMessage,
            row.Location(path: path));
    }

    private static TabularPropertyPath PathOf(
        NodePlan plan,
        MapMember member
    ) => plan.Node.Path is null
        ? TabularPropertyPath.Of(member.Name)
        : plan.Node.Path.Append(member.Name);

    /// <summary>Whether every mapped field of the substructure is present and empty in the source.</summary>
    private static bool IsEmptySubstructure(
        NodePlan plan,
        SourceRecord record,
        ConversionContext context
    )
    {
        foreach (var member in plan.Members)
        {
            if (member.Reader is { } reader
                && !IsEmptyField(reader, record, context))
            {
                return false;
            }

            if (member.Parts is { } parts)
            {
                // WHY: Runs per record; no interface enumerator and no closure over record and context.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < parts.Count; index++)
                {
                    if (!IsEmptyField(parts[index], record, context))
                    {
                        return false;
                    }
                }
            }

            if (member.Nested is { } nested
                && !IsEmptySubstructure(nested, record, context))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a mapped field is present and empty; an unmapped reader counts as empty.</summary>
    private static bool IsEmptyField(
        FieldReader reader,
        SourceRecord record,
        ConversionContext context
    )
    {
        if (reader.ColumnIndex < 0)
        {
            return true;
        }

        var cell = record.CellAt(reader.ColumnIndex);
        if (cell is { Kind: CellKind.Missing }
            && context.Format == TabularFormatKind.Csv)
        {
            return false;
        }

        if (cell.IsBlank)
        {
            return true;
        }

        if (cell.Kind != CellKind.Text)
        {
            return false;
        }

        var text = context.TrimFor(reader.TrimSettings)
            ? cell.Text.Span.Trim()
            : cell.Text.Span;

        return text.IsEmpty;
    }
}
