#! /bin/sh

. ./variables.sh

export JOB_JSON=$(curl --request POST --silent --header "Content-Type: application/json" --data "{\"AssetId\":\"${AZURE_AMS_ASSET_ID}\",\"JobID\":\"1234\",\"Codes\":[\"0x2ADA01\",\"0x2ADA02\",\"0x2ADA03\"]}" "https://${AZURE_API_ENDPOINT}/api/GetPreprocessorJobData?code=${AZURE_API_TOKEN}" )

export JOB_AS_BASE64=$(echo $JOB_JSON | base64 --wrap=0)

export EXTERNAL_UUID=$(uuidgen)

export YAML=$(envsubst < ./submit-job.yaml)

echo $YAML | kubectl create -f -

sleep 10

kubectl logs --follow=true --container='allinone-job' $(kubectl get pods --show-all --selector=job-name=allinone-job-${EXTERNAL_UUID} --output=jsonpath={.items..metadata.name}) 
