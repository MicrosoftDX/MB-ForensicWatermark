
# Docker image

## Requirements for building

To build the docker image locally, the NexGuard installer binaries must be copied into the `k8s/nexguard-installers` directory, specifically

- RPM 1: `NGS_Preprocessor-4.7-123654.el6.x86_64.rpm`
- Key 1: `setupNGS_PreprocessorGPGkey.4.7-123654.x86_64.tar.gz`
- RPM 2: `NexGuard-Streaming_SmartEmbedderCLI-3.5-117278.el6.x86_64.rpm`
- Key 2: `setupNGS_SmartEmbedderCLIGPGKey.3.5-117278.x86_64.tar.gz`

The binaries' versions must match the [Dockerfile](https://github.com/MicrosoftDX/MB-ForensicWatermark/blob/775d02567dd1bfccaa19c051980063e5909a06d3/k8s/Dockerfile#L58-L59
): 

```
ARG PREPROCESSOR_VERSION=4.7-123654
ARG EMBEDDER_VERSION=3.5-117278
```

Also, you should `cp ./variables.sh.template ./variables.sh` and then tweak `variables.sh` appropriately. 

## Local docker image build

Running `build-image-locally.sh` should build the docker image. 

## Preparing to run a demo job

The docker run needs two environment variables, `LICENSES` and `JOB`. These environment variables are JSON documents, which are base64-encoded. 

### LICENSES

- One of the required licenses currently comes from a license server, which IP address is configured in the `PREPROCESSOR_IP` variable in the file `variables.sh`.  
- The other license is stored locally in the filesystem in the directory `k8s/licenses/NGStreamingSE.lic`. 

The licenses must be installed as files in the running docker container, and that happens by passing in the `LICENSES` variable. 

### JOB description

We can fetch a valid job description from a web API endpoint. You need to tweak 



```bash
docker run -it --rm \
    -e "LICENSES=${LICENSES_AS_BASE64}" \
    -e "JOB=${JOB_AS_BASE64}" \
    --entrypoint=/usr/local/bin/dotnet \
    "${acr_name}.azurecr.io/${IMAGE_NAME}:${IMAGE_VERSION}" \
    embedder.dll 
```

