
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

Also, you should `cp ./variables.sh.template ./variables.sh` and then tweak [`variables.sh`](variables.sh) appropriately. 

## Local docker image build

Running `build-image-locally.sh` should build the docker image. 

## Preparing to run a demo job

The docker run needs two environment variables, `LICENSES` and `JOB`. These environment variables are JSON documents, which are base64-encoded. 

### LICENSES

You need two NexGuard licenses, one for the preprocessor, one for the embedder. 

- The proprocessor license currently comes from a license server, which IP address is [configured](https://github.com/MicrosoftDX/MB-ForensicWatermark/blob/master/k8s/variables.sh.template#L3) in the `PREPROCESSOR_IP` variable in the file `variables.sh`.  
- The embedder license is stored locally in the filesystem in the directory `k8s/licenses/NGStreamingSE.lic`. That path is configured in the `EMBEDDER_LICENSE_FILE` variable in the file `variables.sh`.  

All license file contents are aggregated in a [JSON structure](https://github.com/MicrosoftDX/MB-ForensicWatermark/blob/fc1979cdd1020dd7ee06040834e37af70de455cc/k8s/create-license.sh#L11-L15). 

The license JSON structure is then base64-encoded, and passed as `LICENSES` environment variable to the docker container. Inside the docker container, the .NET Core executable [processes the `LICENSES` environment variable and installs the license files into the container's file system](https://github.com/MicrosoftDX/MB-ForensicWatermark/blob/fc1979cdd1020dd7ee06040834e37af70de455cc/k8s/embedder.src/Program.cs#L57-L66). 

In addition to the two licenses, we also ship a custom encoding profile (`k8s/licenses/NGPTV_Preprocessor.xml`) into the container. 

### JOB description

The second thing the job needs is the actual job description. Usually, the job is triggered from outside the k8s cluster. The `JOB` environment variable must contain the base64-encoded JSON Job description. 

### Local job execution on the laptop

All the invocation can be locally simulated using the [`run-in-local-docker.sh`](run-in-local-docker.sh) script, which [creates the license](https://github.com/MicrosoftDX/MB-ForensicWatermark/blob/fc1979cdd1020dd7ee06040834e37af70de455cc/k8s/run-in-local-docker.sh#L5-L20), [fetches a demo job](https://github.com/MicrosoftDX/MB-ForensicWatermark/blob/fc1979cdd1020dd7ee06040834e37af70de455cc/k8s/run-in-local-docker.sh#L22-L24), [provides some diagnostics](https://github.com/MicrosoftDX/MB-ForensicWatermark/blob/fc1979cdd1020dd7ee06040834e37af70de455cc/k8s/run-in-local-docker.sh#L26-L42) and the [launches docker](https://github.com/MicrosoftDX/MB-ForensicWatermark/blob/fc1979cdd1020dd7ee06040834e37af70de455cc/k8s/run-in-local-docker.sh#L46-L51)

```bash
docker run -it --rm \
    -e "LICENSES=${LICENSES_AS_BASE64}" \
    -e "JOB=${JOB_AS_BASE64}" \
    --entrypoint=/usr/local/bin/dotnet \
    "${acr_name}.azurecr.io/${IMAGE_NAME}:${IMAGE_VERSION}" \
    embedder.dll 
```
