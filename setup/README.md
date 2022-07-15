# How to run the test:

1. `cd` into the `setup/docker` folder

2. Run the follow on the docker host:

```sh
sudo bash setup-host.sh python
sudo bash setup-host.sh dotnet
sudo docker-compose up -d
```

3. Wait a few seconds until the containers are running, then test the connection between both containers:

```sh
sudo docker exec nexus-main \
    bash -c "cd /root/nexus-sources-remote; dotnet test --filter Nexus.Sources.Tests.SetupDockerTests"
```