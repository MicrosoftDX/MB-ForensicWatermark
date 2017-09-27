#########################

FROM centos:7.3.1611 as centos-patched

RUN yum update -y \
	&& yum install -y tar gzip glibc.i686 libunwind libicu

# Linted with http://hadolint.lukasmartinelli.ch/

#########################

FROM centos-patched as centos-dotnetcore-sdk

WORKDIR /root

ENV SDKURL=https://download.microsoft.com/download/E/7/8/E782433E-7737-4E6C-BFBF-290A0A81C3D7/dotnet-dev-centos-x64.1.0.4.tar.gz \
	SDKTGZ=dotnet-dev-centos-x64.1.0.4.tar.gz

RUN mkdir -p /opt/dotnet \
	&& mkdir /root/installers \
	&& curl -SL "${SDKURL}" --output "/root/installers/${SDKTGZ}" \
	&& tar zxf "/root/installers/${SDKTGZ}" -C /opt/dotnet \
	&& ln -s /opt/dotnet/dotnet /usr/local/bin \
	&& rm -rf /root/installers/ \
	&& dotnet new --help 

#########################

FROM centos-patched as dotnetcore-on-centos-runtime

ENV RUNTIMEURL=https://download.microsoft.com/download/D/7/A/D7A9E4E9-5D25-4F0C-B071-210CB8267943/dotnet-centos-x64.1.1.2.tar.gz \
    RUNTIMETGZ=dotnet-centos-x64.1.1.2.tar.gz

RUN mkdir -p /opt/dotnet \
	&& mkdir /root/installers && curl -SL "${RUNTIMEURL}" --output "/root/installers/${RUNTIMETGZ}" \
	&& tar zxf "/root/installers/${RUNTIMETGZ}" -C /opt/dotnet \
	&& rm -rf /root/installers \
	&& ln -s /opt/dotnet/dotnet /usr/local/bin

#########################

FROM centos-dotnetcore-sdk as centos-dotnetcore-sdk-builder

WORKDIR /root/embedder.src

ENV HOME=/root \
	APPNAME=embedder

COPY embedder.src .

RUN dotnet restore --runtime centos.7-x64 \
	&& dotnet publish --framework netcoreapp1.1 --runtime centos.7-x64 -c Release --output publish

#########################

FROM dotnetcore-on-centos-runtime as nexguard-finalimage

ARG PREPROCESSOR_VERSION=4.7-123654
ARG EMBEDDER_VERSION=3.5-117278

ENV HOME=/root \
	PREPROCESSOR_RPMFILE=NGS_Preprocessor-${PREPROCESSOR_VERSION}.el6.x86_64.rpm \
	PREPROCESSOR_GPG_TGZ=setupNGS_PreprocessorGPGkey.${PREPROCESSOR_VERSION}.x86_64.tar.gz \
	PREPROCESSOR_GPG_KEY=RPM-GPG-KEY-nexguard \
	EMBEDDER_RPMFILE=NexGuard-Streaming_SmartEmbedderCLI-${EMBEDDER_VERSION}.el6.x86_64.rpm \
	EMBEDDER_GPG_TGZ=setupNGS_SmartEmbedderCLIGPGKey.${EMBEDDER_VERSION}.x86_64.tar.gz \
	EMBEDDER_GPG_KEY=RPM-GPG-KEY-civolution \
	APPNAME=embedder \
	DOTNET_CLI_TELEMETRY_OPTOUT=1

COPY nexguard-installers/ ${HOME}/
COPY --from=centos-dotnetcore-sdk-builder ${HOME}/embedder.src/publish /root/${APPNAME}

WORKDIR ${HOME}

RUN tar zxvf "${HOME}/${PREPROCESSOR_GPG_TGZ}" -C "${HOME}" \
	&& rpm --import "${PREPROCESSOR_GPG_KEY}" \
	&& yum install -y "${HOME}/${PREPROCESSOR_RPMFILE}" \
	&& rm -rf "${PREPROCESSOR_GPG_TGZ}" "${PREPROCESSOR_GPG_KEY}" "${PREPROCESSOR_RPMFILE}" \
	&& tar zxvf "${HOME}/${EMBEDDER_GPG_TGZ}" -C "${HOME}" \
	&& rpm --import "${EMBEDDER_GPG_KEY}" \
	&& yum install -y "${HOME}/${EMBEDDER_RPMFILE}" \
	&& rm -rf "${EMBEDDER_GPG_TGZ}" "${EMBEDDER_GPG_KEY}" "${EMBEDDER_RPMFILE}" \
	&& rm "${HOME}/${APPNAME}/${APPNAME}.runtimeconfig.json"

WORKDIR ${HOME}/${APPNAME}

CMD [ "/usr/local/bin/dotnet", "${HOME}/${APPNAME}/${APPNAME}.dll" ]
