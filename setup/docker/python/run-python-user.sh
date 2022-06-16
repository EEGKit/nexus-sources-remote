red=$'\e[0;31m'
green=$'\e[0;32m'
orange=$'\e[0;33m'
white=$'\e[0m'

echo "Continue as: ${green}$(whoami)${white}"

# check if git project exists
if [ ! -d 'repository' ]; then
    mkdir -p 'repository'
fi

cd 'repository'

if [ -d '.git' ]; then
    echo "Pull changes"
    git fetch origin main
    git reset --hard origin/main
else
    echo "Clone repository ${yellow}$1${white}"
    git clone $1 .
fi

# prepare python environment
if [ -d "requirements.txt" ]; then
    env="~/venv"

    if [ ! -d $env ]; then 
        python3 -m venv $env 
    fi

    source $env/bin/activate
    python -m pip install --pre --index-url https://www.myget.org/F/apollo3zehn-dev/python/ -r "requirements.txt"
fi

# run user code (finally!)
shift
$@