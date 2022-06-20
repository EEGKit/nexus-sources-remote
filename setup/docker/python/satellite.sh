green=$'\e[0;32m'
orange=$'\e[0;33m'
white=$'\e[0m'

echo "${green}Welcome to the satellite.sh script!${white}"

# virtual environment
env="~/venv"

if [ ! -d $env ]; then 
    echo "Create virtual environment ${green}${env}${white}"
    python3 -m venv $env 
fi

echo "Activate virtual environment ${green}${env}${white}"
source $env/bin/activate

if [ -f "requirements.txt" ]; then

    if [ -f "../commit_changed" ]; then
        echo "${green}Install requirements${white}"
        python -m pip install --pre --index-url https://www.myget.org/F/apollo3zehn-dev/python/ -r "requirements.txt" --disable-pip-version-check
    fi

else
    echo "${orange}No requirements.txt found${white}"
fi

# run user code
echo "${green}Run user code${white}"

shift
python $@