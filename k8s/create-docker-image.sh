#!/bin/sh

IMAGE_NAME=$1
IMAGE_VERSION=$2
dockerfile="./Dockerfile"

cd /git/k8s

/usr/local/bin/dockerd \
    --host=unix:///var/run/docker.sock \
    --storage-driver=vfs &

docker version

apk add --no-cache jq curl

DOCKER_REGISTRY=$(echo $DOCKER_SECRET_CFG | jq -r '. | keys[0]' | sed --expression="s_https://__")
DOCKER_USERNAME=$(echo $DOCKER_SECRET_CFG | jq -r '.[. | keys[]].username')
DOCKER_PASSWORD=$(echo $DOCKER_SECRET_CFG | jq -r '.[. | keys[0]].password')

docker login "${DOCKER_REGISTRY}" \
       --username "${DOCKER_USERNAME}" \
       --password "${DOCKER_PASSWORD}"

docker build \
       --tag "${DOCKER_REGISTRY}/${IMAGE_NAME}:${IMAGE_VERSION}" \
       --file "${dockerfile}" \
       .

docker push "${DOCKER_REGISTRY}/${IMAGE_NAME}:${IMAGE_VERSION}"

kill -9 $(cat /var/run/docker.pid)

echo "Stopped docker daemon"
