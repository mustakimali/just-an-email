.PHONY: init build dev frontend clean

init:
	cd frontend && npm install
	cargo build

build: frontend
	cargo build --release

frontend:
	cd frontend && npm run build

dev:
	cd frontend && npm run dev &
	cargo run

clean:
	rm -rf frontend/dist frontend/node_modules
	cargo clean
