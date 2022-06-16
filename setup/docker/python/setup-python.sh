green=$'\e[0;32m'
white=$'\e[0m'
echo "${green}Welcome to the setup-python script"'!'"${white}"
echo "${green}Setting up SSH server ...${white}"

apt update
apt install openssh-server -y
sed -i 's/#PermitRootLogin prohibit-password/PermitRootLogin yes/g' '/etc/ssh/sshd_config'
service ssh start

echo "${green}Setting up SSH server ... Done.${white}"

echo "${green}Loading run-python.sh script ...${white}"
wget -q -O 'https://raw.githubusercontent.com/Nexusforge/nexus-sources-remote/main/setup/docker/python/run-python.sh'
echo "${green}Loading run-python.sh script ... Done${white}"

trap : TERM INT
sleep infinity & wait
