FROM public.ecr.aws/lambda/dotnet:10

WORKDIR /var/task

# Copia a saida do publish gerada no host (docker-host-build-output-dir
# em aws-lambda-tools-defaults.json) para dentro da imagem.
COPY "bin/Release/lambda-publish" .
