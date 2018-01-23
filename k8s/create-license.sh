#!/bin/bash

. ./variables.sh

PREPROCESSOR_LIC="server=${PREPROCESSOR_IP}
port=5093"

cat > PayTVPreProcessorVideo.lic <<-EOF
	server=${PREPROCESSOR_IP}
	port=5093
EOF

PREPROCESSOR_LIC_BASE64=$(echo "${PREPROCESSOR_LIC}" | base64 --wrap=0)

EMBEDDER_LICENSE_BASE64=$(cat "${EMBEDDER_LICENSE_FILE}" | base64 --wrap=0)

ENCODER_SETTINGS_BASE64=$(cat "${ENCODER_SETTINGS_FILE}" | base64 --wrap=0)

export LIC="{ \"Licenses\": [ 
	{ \"Path\" : \"/usr/share/nexguardescreener-preprocessor/PayTVPreProcessorVideo.lic\", \"Content\" : \"${PREPROCESSOR_LIC_BASE64}\" }, 
	{ \"Path\" : \"/usr/bin/NGStreamingSE.lic\", \"Content\" : \"${EMBEDDER_LICENSE_BASE64}\" }, 
	{ \"Path\" : \"/usr/share/nexguardescreener-preprocessor/NGPTV_Preprocessor.xml\", \"Content\" : \"${ENCODER_SETTINGS_BASE64}\" } 
] }"

LICENSES_AS_BASE64=$(echo $LIC | base64 --wrap=0)

cat > lic.yaml <<-EOF
	# kubectl replace -f licenses.yaml
	apiVersion: v1
	kind: Secret
	metadata:
	  name: licenses
	type: Opaque
	data:
	  licenses: $(echo $LICENSES_AS_BASE64 | base64 --wrap=0)
EOF

kubectl create -f lic.yaml

# kubectl create configmap encoder-settings --from-file="NGPTV_Preprocessor.xml=${ENCODER_SETTINGS_FILE}"

kubectl create configmap encoder-settings --from-file="PayTVPreProcessorVideo.lic=PayTVPreProcessorVideo.lic" --from-file="NGStreamingSE.lic=${EMBEDDER_LICENSE_FILE}" --from-file="NGPTV_Preprocessor.xml=${ENCODER_SETTINGS_FILE}" 

# kubectl get configmaps encoder-settings -o yaml

kubectl create secret docker-registry \
	"${acr_name}.azurecr.io" \
	--docker-server="${acr_name}.azurecr.io" \
	--docker-username="${acr_name}" \
	--docker-password="${acr_pass}" \
	--docker-email="root@${acr_name}"
