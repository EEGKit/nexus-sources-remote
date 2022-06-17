red=$'\e[0;31m'
green=$'\e[0;32m'
orange=$'\e[0;33m'
white=$'\e[0m'

locked()
{
    >&2 echo "${red}Unable to acquire the file lock${white}"
    exit 1
}

if (( $# < 2 )); then
    >&2 echo "${red}Illegal number of parameters${white}"
    exit 1
fi

echo "The git-url is: ${green}$1${white}"

# derive user id
user_id=$(echo -n $1 | openssl dgst -binary -md5 | openssl base64)
user_id=${user_id//\//_}
echo "Derived user id: ${green}$user_id${white}"

(
    # get lock (wait max 10 s)
    flock -w 10 100 || locked

    # get or add user
    if id $user_id &>/dev/null; then
        echo "User ${green}exists${white}"
        # password=$(<"password-store/$user_id")
    else
        echo "User ${orange}does not exist${white}"
        password=$(cat /dev/urandom | tr -dc a-zA-Z0-9 | fold -w 14 | head -n 1)
        mkdir -p "password-store"
        echo $password > "password-store/$user_id"
        useradd -p $password $user_id
        echo "Created user ${green}$user_id${white}"
    fi

    # prepare user folder
    mkdir -p "/home/$user_id"
    cp "run-user.sh" "/home/$user_id/run-user.sh"
    cp "satellite.sh" "/home/$user_id/satellite.sh"
    chown -R $user_id:$user_id "/home/$user_id"
    
) 100>"/tmp/run-$user_id.lock"

# continue as $user_id
cd "/home/$user_id"
command="bash run-user.sh $@"
su $user_id -c "$command"