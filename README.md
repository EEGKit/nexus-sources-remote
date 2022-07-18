# Nexus Remote Data Source

[![GitHub Actions](https://github.com/malstroem-labs/nexus-sources-remote/actions/workflows/build-and-publish.yml/badge.svg?branch=main)](https://github.com/malstroem-labs/nexus-sources-remote/actions)

The RPC data source is different from normal data sources in that it allows any code to be executed which imposes a security risk. Additionally, the command to be executed (e.g. python) might not exist in the current environment. Nexus is distributed as Docker Container with minimum dependencies. Therefore another solution must exist to extend the system. An option would be to use Docker-in-Docker (`dind`) and start a new container which is just another command for the RCP data source. The container itself could be a calculation-only environment with network access (but no drive access).

Since the RPC command can be anything its use must be regulated.

## Process
`GET extensions / static-parameters` + "rpc" and "rpc:available-commands")

1. Show available RPC commands (e.g. `docker run --volume /volume1/Daten/users/{nexus:registering-user}:/data python:3.7.0 bash -c {docker-command}`)
2. User selects a command
3. Ask for command arguments (1x ask for each variable in command, e.g. `{username}` and `{docker-command}`).
4. User provides arguments:

______________________________

> Example: Python

Files:
 - data-source.py
 - requirements.txt

`{docker-command}` =

```properties
env="/data/env"
if [ ! -d $env ] then python3 -m venv $env fi;
source $env/bin/activate
python3 /data/bin/pip install -r /data/my-data-source/requirements.txt;
python3 /data/my-data-source/data-source.py {rpc-port} {connection-id}
```

dependencies: Nexus.Extensibility

______________________________


> Example: .NET

Files:
 - data-source.csproj
 - data-source.cs

`{docker-command}` =

```properties
dotnet run data-source.csproj {rpc-port} {connection-id}
```

dependencies: Nexus.Extensibility

______________________________

5. Register

`POST backendsources / register` + Username, Type = RpcDataSource, ResourceLocator, Configuration (with full docker command template + Configuration parameters (= command)). 

This way RpcDataSource can take the docker command, compare to available commands and interpolate all missing parameters (here: the `docker-command` and the `rpc-port`). 

(triggers catalogs reload)

6. Verify

`GET backendsources`

7. Invoke

Nexus merges `backend source config` and `global-config` (e.g. `rpc:available-commands:0:docker ...`) and `nexus:registering-user`). When merge conflicts occur, `global-config` should win.

Nexus then provides the merged config to the Rpc data source instance:

Rpc data source ...
- gets instantiated with configuration values like `command`, `nexus:registering-user` and `docker-command`
- interpolates `command` with missing values (multiple iterations to replace `rpc-port` and `connection-id` also)
- executes command
- does what rpc data sources do (communicate)

