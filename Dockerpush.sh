#!/bin/bash

get_version() {
  date '+%y%m%d_%H%M'
}

if [[ -z "$dock_version" ]]; then
    dock_version=$(get_version)
fi

echo "Building image version:$dock_version"

docker build . -t justaml

docker tag justaml mustakimali/justaml:latest
docker push mustakimali/justaml:latest

tag="mustakimali/justaml:$dock_version"
docker tag justaml $tag
docker push mustakimali/justaml:$dock_version

docker rmi justaml
docker rmi $tag
docker rmi mustakimali/justaml:latest

echo "Tagged mustakimali/justaml:$dock_version"

kubectl set image deployments/justaml *=mustakimali/justaml:${dock_version}
kubectl -n hnpreload rollout status deployments/justaml -w