1. `cd` into the `setup/docker` folder

2. Run the follow on the docker host:

```sh
sudo bash setup-host.sh python
sudo docker-compose --env-file python/.env up -d
```

3. Wait a few seconds until the containers are running, then test the connection between both containers:

```sh
sudo docker exec nexus-main ssh root@nexus-python bash run-python-root.sh https://github.com/Nexusforge/nexus-remoting-sample python main.py
```