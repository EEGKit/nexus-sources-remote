green=$'\e[0;32m'
white=$'\e[0m'
echo "${green}Welcome to the setup-python script"'!'"${white}"

echo "${green}Set up SSH server${white}"
apt update
apt install openssh-server -y
sed -i 's/#PermitRootLogin prohibit-password/PermitRootLogin yes/g' '/etc/ssh/sshd_config'
service ssh start

echo "${green}Load run-python-root.sh and run-python-user.sh scripts${white}"
curl -s -O 'https://raw.githubusercontent.com/Nexusforge/nexus-sources-remote/main/setup/docker/python/run-python-root.sh'
curl -s -O 'https://raw.githubusercontent.com/Nexusforge/nexus-sources-remote/main/setup/docker/python/run-python-user.sh'

trap : TERM INT
sleep infinity & wait
