1. Run the follow on the docker host BEFORE docker-compose up -d:

```sh
# generate SSH key for container 'nexus-main'
shared_folder_1='/var/lib/nexus/docker/nexus-main'
mkdir -p "${shared_folder_1}/.ssh"
ssh-keygen -q -t rsa -N '' -f "${shared_folder_1}/.ssh/id_rsa" <<<y >/dev/null 2>&1

# generate SSH key for container 'nexus-python'
shared_folder_2='/var/lib/nexus/docker/nexus-python'
mkdir -p "${shared_folder_2}/.ssh"
ssh-keygen -q -t rsa -N '' -f "${shared_folder_2}/.ssh/id_rsa" <<<y >/dev/null 2>&1

# exchange keys
cat "${shared_folder_1}/.ssh/id_rsa.pub" > "${shared_folder_2}/.ssh/authorized_keys"

echo "StrictHostKeyChecking accept-new" > "${shared_folder_1}/.ssh/config"
```

2. Start containers
```sh
sudo docker-compose up -d
```

3. Run the follow on the docker host AFTER docker-compose up -d:
```sh
sudo docker exec nexus-main ssh root@nexus-python echo 'The SSH connection works!'
```