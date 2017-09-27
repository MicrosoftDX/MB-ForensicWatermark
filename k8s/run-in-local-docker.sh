#!/bin/sh

. ./variables.sh

PREPROCESSOR_LIC="server=${PREPROCESSOR_IP}
port=5093"

PREPROCESSOR_LIC_BASE64=$(echo "${PREPROCESSOR_LIC}" | base64 --wrap=0)

EMBEDDER_LICENSE_BASE64=$(cat "${EMBEDDER_LICENSE_FILE}" | base64 --wrap=0)

ENCODER_SETTINGS_BASE64=$(cat "${ENCODER_SETTINGS_FILE}" | base64 --wrap=0)

LIC="{ \"Licenses\": [ 
	{ \"Path\" : \"/usr/share/nexguardescreener-preprocessor/PayTVPreProcessorVideo.lic\", \"Content\" : \"${PREPROCESSOR_LIC_BASE64}\" }, 
	{ \"Path\" : \"/usr/bin/NGStreamingSE.lic\", \"Content\" : \"${EMBEDDER_LICENSE_BASE64}\" }, 
	{ \"Path\" : \"/usr/share/nexguardescreener-preprocessor/NGPTV_Preprocessor.xml\", \"Content\" : \"${ENCODER_SETTINGS_BASE64}\" } 
] }"

LICENSES_AS_BASE64=$(echo $LIC | base64 --wrap=0)

JOB_JSON=$(curl --request POST --silent --header "Content-Type: application/json" --data "{\"AssetId\":\"${AZURE_AMS_ASSET_ID}\",\"JobID\":\"1234\",\"Codes\":[\"0x2ADA01\",\"0x2ADA02\",\"0x2ADA03\"]}" "https://${AZURE_API_ENDPOINT}/api/GetPreprocessorJobData?code=${AZURE_API_TOKEN}" )

JOB_AS_BASE64=$(echo $JOB_JSON | base64 --wrap=0)

echo Generated all data

echo $LICENSES_AS_BASE64 | base64 -d > __licenses.json
echo $JOB_AS_BASE64      | base64 -d > __job.json
echo --------------------------------------------
echo PayTVPreProcessorVideo.lic
echo $LICENSES_AS_BASE64 | base64 -d | jq -r '.Licenses[0].Content' | base64 -d
echo
echo --------------------------------------------
echo NGStreamingSE.lic
echo $LICENSES_AS_BASE64 | base64 -d | jq -r '.Licenses[1].Content' | base64 -d
echo
echo --------------------------------------------
echo NGPTV_Preprocessor.xml
echo $LICENSES_AS_BASE64 | base64 -d | jq -r '.Licenses[2].Content' | base64 -d | head
echo ...
echo --------------------------------------------

echo Kicking off docker

docker run -it --rm \
    -e "LICENSES=${LICENSES_AS_BASE64}" \
    -e "JOB=${JOB_AS_BASE64}" \
    --entrypoint=/usr/local/bin/dotnet \
    "${acr_name}.azurecr.io/${IMAGE_NAME}:${IMAGE_VERSION}" \
    embedder.dll
