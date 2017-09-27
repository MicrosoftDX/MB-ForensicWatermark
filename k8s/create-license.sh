#!/bin/bash

. ./variables.sh

PREPROCESSOR_LIC="server=${PREPROCESSOR_IP}
port=5093"

PREPROCESSOR_LIC_BASE64=$(echo "${PREPROCESSOR_LIC}" | base64 --wrap=0)
EMBEDDER_LICENSE_BASE64=$(cat "${EMBEDDER_LICENSE_FILE}" | base64 --wrap=0)
ENCODER_SETTINGS_BASE64=$(cat "${ENCODER_SETTINGS_FILE}" | base64 --wrap=0)
LIC="{ 'Licenses': [ 
	{ 'Path' : '/usr/share/nexguardescreener-preprocessor/PayTVPreProcessorVideo.lic', 'Content' : '${PREPROCESSOR_LIC_BASE64}' }, 
	{ 'Path' : '/usr/bin/NGStreamingSE.lic', 'Content' : '${EMBEDDER_LICENSE_BASE64}' }, 
	{ 'Path' : '/usr/share/nexguardescreener-preprocessor/NGPTV_Preprocessor.xml', 'Content' : '${ENCODER_SETTINGS_BASE64}' } 
	] }"

cat > lic.yaml <<-EOF
	# kubectl replace -f licenses.yaml
	apiVersion: v1
	kind: Secret
	metadata:
	  name: licenses
	type: Opaque
	data:
	  licenses: $(echo $LIC | base64 --wrap=0 | base64 --wrap=0)
EOF

kubectl create -f lic.yaml
