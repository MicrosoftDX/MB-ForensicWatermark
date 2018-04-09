#!/bin/bash

. ./variables.sh

DOCKER_REGISTRY="${acr_name}.azurecr.io"

docker build --tag "${DOCKER_REGISTRY}/${IMAGE_NAME}:${IMAGE_VERSION}" --file ./Dockerfile .

# acr_pass=$(az acr credential show --name $acr_name | jq -r .passwords[0].value)

echo "${acr_pass}" | docker login "${DOCKER_REGISTRY}" --username "${acr_name}" --password-stdin

docker push "${DOCKER_REGISTRY}/${IMAGE_NAME}:${IMAGE_VERSION}"
