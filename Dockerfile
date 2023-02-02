# Set the base image as the .NET 7.0 SDK (this includes the runtime)
FROM mcr.microsoft.com/dotnet/sdk:7.0 as build-env

# Copy everything and publish the release (publish implicitly restores and builds)
WORKDIR /app
COPY . ./
RUN dotnet publish ./Dustuu.Actions.BunnyCdnDeploy/Dustuu.Actions.BunnyCdnDeploy.csproj -c Release -o out --no-self-contained

# Label the container
LABEL maintainer="Dustuu <dustuu@furcade.com>"
LABEL repository="https://github.com/dotnet/samples"
LABEL homepage="https://github.com/dotnet/samples"

# Label as GitHub action
LABEL com.github.actions.name="BunnyCDN Test"
# Limit to 160 characters
LABEL com.github.actions.description="Upload files to BunnyCDN"
# See branding:
# https://docs.github.com/actions/creating-actions/metadata-syntax-for-github-actions#branding
LABEL com.github.actions.icon="upload-cloud"
LABEL com.github.actions.color="orange"

# Relayer the .NET Runtime, anew with the build output
FROM mcr.microsoft.com/dotnet/runtime:7.0
COPY --from=build-env /app/out .
ENTRYPOINT [ "dotnet", "/Dustuu.Actions.BunnyCdnDeploy.dll" ]