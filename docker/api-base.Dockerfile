# The base image for the API container: the stock ASP.NET runtime the SDK would otherwise use, plus git for the wiki's
# version history and the SSH client for pushing it to a backup remote. The CD workflow builds it on the VM and the
# API project picks it up through the ApiBaseImage property.
FROM mcr.microsoft.com/dotnet/aspnet:10.0

RUN apt-get update \
    && apt-get install --yes --no-install-recommends git openssh-client \
    && rm -rf /var/lib/apt/lists/*

# The SDK only defaults to the image's non-root user for Microsoft's own images, so set it here.
USER $APP_UID
