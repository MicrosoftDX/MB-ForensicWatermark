#!/bin/bash

. ./variables.sh

export JOB_JSON="$(cat job.json)"
export JOB_AS_BASE64="$(echo $JOB_JSON | base64 --wrap=0)"
export EXTERNAL_UUID="$(uuidgen)"

envsubst < submit-job-tempdisk.yaml | kubectl create -f -

# sleep 10

# kubectl logs --follow=true --container='allinone-job' $(kubectl get pods --show-all --selector=job-name=allinone-job-${EXTERNAL_UUID} --output=jsonpath={.items..metadata.name}) 
