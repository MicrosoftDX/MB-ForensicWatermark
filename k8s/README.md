
# Docker image

## Requirements for building

To build the docker image locally, the NexGuard installer binaries must be copied into the `k8s/nexguard-installers` directory, specifically

- RPM 1: `NGS_Preprocessor-4.7-123654.el6.x86_64.rpm`
- Key 1: `setupNGS_PreprocessorGPGkey.4.7-123654.x86_64.tar.gz`
- RPM 2: `NexGuard-Streaming_SmartEmbedderCLI-3.5-117278.el6.x86_64.rpm`
- Key 2: `setupNGS_SmartEmbedderCLIGPGKey.3.5-117278.x86_64.tar.gz`

Also, you should `cp ./variables.sh.template ./variables.sh` and then tweak `variables.sh` appropriately. 

## Local docker image build

Running `build-image-locally.sh` should build the docker image. 
