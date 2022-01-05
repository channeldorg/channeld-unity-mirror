FROM ubuntu
ENV CHANNELD_IP=127.0.0.1
COPY ./Build/Linux /server/
WORKDIR /server

CMD ./server.x86_64 -sa $CHANNELD_IP -spawnai 100