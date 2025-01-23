FROM docker.io/python:3.12-slim

COPY src/remoting/python /nexus_remoting
COPY src/agent/python /app

WORKDIR "/app"

RUN apt update &&\
    apt install git -y &&\
    useradd -m -u 1654 app &&\
    pip install -r requirements.txt

USER app
ENV PYTHONPATH /nexus_remoting

CMD ["fastapi", "run", "main.py"]