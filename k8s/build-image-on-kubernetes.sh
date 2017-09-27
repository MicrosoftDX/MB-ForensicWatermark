#!/bin/sh

. ./variables.sh

export EXTERNAL_UUID=$(uuidgen)
export EXTERNAL_GIT_ORIGIN=$(git remote get-url origin)

envsubst < build-job.yaml | kubectl create -f -

sleep 10

kubectl logs --follow=true --container='build-job' $(kubectl get pods --show-all --selector=job-name=build-job-${EXTERNAL_UUID} --output=jsonpath={.items..metadata.name}) 
