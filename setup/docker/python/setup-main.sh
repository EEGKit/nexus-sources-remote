green=$'\e[0;32m'
white=$'\e[0m'
echo "${green}Welcome to the setup-test script"'!'"${white}"
echo "${green}Set up SSH client${white}"

apt update
apt install openssh-client -y

trap : TERM INT
sleep infinity & wait
