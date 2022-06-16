1. Run the follow on the docker host:

```sh
sudo bash setup-host.sh
sudo docker-compose up -d
```

2. Wait a few seconds until the containers are running, then test the SSH connection between both containers:

```sh
sudo docker exec nexus-main ssh root@nexus-python echo 'The SSH connection works!'
```