green=$'\e[0;32m'
orange=$'\e[0;33m'
white=$'\e[0m'

# prepare python environment
if [ -f "requirements.txt" ]; then
    echo "${green}Set up virtual environment${white}"
    env="~/venv"

    if [ ! -d $env ]; then 
        python3 -m venv $env 
    fi

    source $env/bin/activate

    echo "${green}Install requirements${white}"
    python -m pip install --pre --index-url https://www.myget.org/F/apollo3zehn-dev/python/ -r "requirements.txt" --disable-pip-version-check
else
    echo "${orange}No requirements.txt found${white}"
fi

# run user code (finally!)
shift
$@