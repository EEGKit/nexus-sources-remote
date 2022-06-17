green=$'\e[0;32m'
white=$'\e[0m'

satellite_id=$1

# Generate key for main container and add config file, but do not override if it already exists
main_folder='/var/lib/nexus/docker/nexus-main'

if [ ! -d '/var/lib/nexus/docker/nexus-main' ]; then   
    echo "Generate SSH key for container ${green}nexus-main${white}"
    mkdir -p "${main_folder}/.ssh"
    ssh-keygen -q -t rsa -N '' -f "${main_folder}/.ssh/id_rsa" <<<y >/dev/null 2>&1
    echo "StrictHostKeyChecking no" > "${main_folder}/.ssh/config"
    echo "UserKnownHostsFile=/dev/null" >> "${main_folder}/.ssh/config"
fi

# Generate key for satellite container and add main container key to authorized keys file
echo "Generate SSH key for container ${green}nexus-${satellite_id}${white}"
satellite_folder="/var/lib/nexus/docker/nexus-${satellite_id}"
mkdir -p "${satellite_folder}/.ssh"
ssh-keygen -q -t rsa -N '' -f "${satellite_folder}/.ssh/id_rsa" <<<y >/dev/null 2>&1
cat "${main_folder}/.ssh/id_rsa.pub" > "${satellite_folder}/.ssh/authorized_keys"