1. Run the follow on the docker host BEFORE docker-compose up -d:

```sh
shared_ssh_folder=/var/lib/nexus/.ssh
sudo mkdir -p ${shared_ssh_folder}
sudo ssh-keygen -q -t rsa -N '' -f ${shared_ssh_folder}/id_rsa <<<y >/dev/null 2>&1
cat ${shared_ssh_folder}/id_rsa.pub > ${shared_ssh_folder}/authorized_keys
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