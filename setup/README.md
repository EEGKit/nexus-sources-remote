1. Run the follow on the docker host BEFORE docker-compose up -d:

```sh
# generate SSH key for container 'nexus-main'
shared_ssh_folder_1=/var/lib/nexus/docker/nexus-main/.ssh
sudo mkdir -p ${shared_ssh_folder_1}
sudo ssh-keygen -q -t rsa -N '' -f ${shared_ssh_folder_1}/id_rsa <<<y >/dev/null 2>&1

# generate SSH key for container 'nexus-python'
shared_ssh_folder_2=/var/lib/nexus/docker/nexus-python/.ssh
sudo mkdir -p ${shared_ssh_folder_2}
sudo ssh-keygen -q -t rsa -N '' -f ${shared_ssh_folder_2}/id_rsa <<<y >/dev/null 2>&1

# exchange keys
cat ${shared_ssh_folder_1}/id_rsa.pub > ${shared_ssh_folder_2}/authorized_keys

echo "nexus-python" > ${shared_ssh_folder_1}/known_hosts
cat ${shared_ssh_folder_2}/id_rsa.pub >> ${shared_ssh_folder_1}/known_hosts
echo "" >> ${shared_ssh_folder_1}/known_hosts
```

2. Start containers
```sh
sudo docker-compose up -d
```

3. Run the follow on the docker host AFTER docker-compose up -d:
```sh
sudo docker exec test ssh -oStrictHostKeyChecking=accept-new \
root@nexus-python echo "The SSH connection works!"
```