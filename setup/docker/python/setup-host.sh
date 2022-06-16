green=$'\e[0;32m'
white=$'\e[0m'

echo "${green}Generate SSH key for container 'nexus-main'${white}"
shared_folder_1='/var/lib/nexus/docker/nexus-main'
mkdir -p "${shared_folder_1}/.ssh"
ssh-keygen -q -t rsa -N '' -f "${shared_folder_1}/.ssh/id_rsa" <<<y >/dev/null 2>&1

echo "${green}Generate SSH key for container 'nexus-python'${white}"
shared_folder_2='/var/lib/nexus/docker/nexus-python'
mkdir -p "${shared_folder_2}/.ssh"
ssh-keygen -q -t rsa -N '' -f "${shared_folder_2}/.ssh/id_rsa" <<<y >/dev/null 2>&1

echo "${green}Populate authorized_keys and config file${white}"
cat "${shared_folder_1}/.ssh/id_rsa.pub" > "${shared_folder_2}/.ssh/authorized_keys"
echo "StrictHostKeyChecking no" > "${shared_folder_1}/.ssh/config"
echo "UserKnownHostsFile=/dev/null" >> "${shared_folder_1}/.ssh/config"