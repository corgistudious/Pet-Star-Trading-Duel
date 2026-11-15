#!/usr/bin/env bash
# Self-signed certificate for local/dev testing only. Staging/production use Let's Encrypt (certbot) certificates.
set -euo pipefail
cd "$(dirname "$0")/../nginx" && mkdir -p certs
openssl req -x509 -nodes -newkey rsa:2048 -days 30 -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,DNS:admin.localhost" -keyout certs/privkey.pem -out certs/fullchain.pem
echo "dev certs written to deployment/nginx/certs"
