VERSION?=local
IMAGE_NAME?=mustakimali/justaml

.PHONY: build
build:
	@dotnet build

.PHONY: run
run: build
	@dotnet run

.PHONY: test
test: build
	dotnet test

.PHONY: docker-build
docker-build:
	docker build -t $(IMAGE_NAME):$(VERSION) -f Dockerfile .
	
.PHONY: docker-run
docker-run:
	docker run -ti --rm --name image-proxy --publish 5000:80 $(IMAGE_NAME):$(VERSION)

.PHONY: docker-push
docker-push:
	./Dockerpush.sh