1. Run the follow on the docker host:

```sh
sudo bash setup-host.sh
sudo docker-compose up -d
```

2. Wait a few seconds until the containers are running, then test the connection between both containers:

```sh
sudo docker exec nexus-main \
    bash -c "cd /root/nexus-sources-remote; dotnet build; dotnet test --filter Nexus.Sources.Tests.SetupDockerPythonTests"
```