# Database images

## Images

`database-images.Dockerfile` pins every image by tag and multi-platform index
digest.

| Stage | Image | Used by |
| --- | --- | --- |
| `mariadb1011` | `mariadb:10.11.19` | MySQL tests, `compose.yml` |
| `mariadb114` | `mariadb:11.4.13` | MySQL tests, `compose.yml` |
| `mariadb118` | `mariadb:11.8.9` | MySQL tests, `compose.yml` |
| `mariadb123` | `mariadb:12.3.3` | MySQL tests, samples, `compose.yml` |
| `mysql84` | `mysql:8.4.13` | MySQL tests, `compose.yml` |
| `mysql97` | `mysql:9.7.4` | MySQL tests, `compose.yml` |
| `postgres` | `postgres:18.7` | PostgreSQL tests, `compose.yml` |

## How tests use the images

The database tests and samples embed `database-images.Dockerfile` as a
resource and read the `FROM` line of their stage; the MySQL tests take the
stage from `DOKA_TABULAR_MYSQL_TARGET`. Testcontainers starts one server per
test run from the pinned image and removes it when the run ends, so no server
outlives a run.

## Update an image

Change the tag and the index digest of the stage in the same commit as the
test changes the new version needs. The current index digest of a tag:

```sh
docker buildx imagetools inspect mariadb:12.3.3 --format '{{json .Manifest.Digest}}'
```

Expected: the digest of the multi-platform index in quotes, `"sha256:..."`;
without the quotes it is the value after `@` in the `FROM` line.

## Run a database locally

`compose.yml` has one profile per stage. Start a server and wait until it is
healthy:

```sh
docker compose -f docker/compose.yml --profile mariadb123 up -d --wait
```

Expected: `Container doka-tabular-mariadb123-1 Healthy`. Run a sample against
it:

```sh
DOKA_TABULAR_SAMPLE_SERVER=mariadb123 dotnet run --project samples/Doka.Tabular.Samples.Journals
```

The sample recreates its database at its start, named after the last part of
its namespace, here `tabular_sample_journals`, and leaves its data there until
its next run. The samples connect to the MariaDB
and MySQL stages only.

## Connect from an IDE

| Stage | Host and port | Database | User | Password |
| --- | --- | --- | --- | --- |
| `mariadb1011` | `127.0.0.1:34011` | `tabular` | `tabular` | `tabular-local` |
| `mariadb114` | `127.0.0.1:34014` | `tabular` | `tabular` | `tabular-local` |
| `mariadb118` | `127.0.0.1:34018` | `tabular` | `tabular` | `tabular-local` |
| `mariadb123` | `127.0.0.1:34023` | `tabular` | `tabular` | `tabular-local` |
| `mysql84` | `127.0.0.1:34084` | `tabular` | `tabular` | `tabular-local` |
| `mysql97` | `127.0.0.1:34097` | `tabular` | `tabular` | `tabular-local` |
| `postgres` | `127.0.0.1:54418` | `tabular` | `tabular` | `tabular-local` |

The databases of the samples are read with the user `root` and the password
`tabular-local-root`. These are local defaults: `TABULAR_DEV_DATABASE`,
`TABULAR_DEV_USER`, `TABULAR_DEV_PASSWORD`, `TABULAR_DEV_ROOT_PASSWORD` and
one port variable per stage, the stage name in capitals, for example
`TABULAR_DEV_MARIADB123_PORT`, override them for `compose.yml` and the
samples.

## Stop and clean up

```sh
docker compose -f docker/compose.yml --profile mariadb123 down
```

stops the server and keeps its data. This command deletes the data as well:

```sh
docker compose -f docker/compose.yml --profile mariadb123 down --volumes
```
