namespace Doka.Tabular;

internal sealed record EnumMember(
    string Name,
    string? AttributeText,
    ulong Raw
)
{
    public string FixedText(
        bool memberName
    ) => memberName
        ? Name
        : AttributeText ?? Name;
}
