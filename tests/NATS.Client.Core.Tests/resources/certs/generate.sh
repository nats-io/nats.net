#!/bin/bash

set -e

# -subj values may fail on Windows if running under MSYSGIT
# Make sure to disable POSIX-to-Windows path conversion e.g.
# export MSYS_NO_PATHCONV=1
#

rm -rf store
mkdir store

echo ================================
echo CA
echo ================================
openssl genrsa -out store/ca.key 4096
openssl req -x509 -sha256 -new -nodes -key store/ca.key -days 3650 -out store/ca.crt -subj '/CN=ca'
openssl x509 -noout -text -in store/ca.crt
cp store/ca.crt ca-cert.pem

echo ================================
echo SERVER
echo ================================
cat <<EOF > store/server.ext
authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage = digitalSignature, nonRepudiation, keyEncipherment, dataEncipherment
subjectAltName = @alt_names
[alt_names]
DNS.1 = localhost
IP.1 = 127.0.0.1
IP.2 = ::1
EOF
openssl req -new -nodes -out store/server.csr -newkey rsa:4096 -keyout store/server.key -subj '/CN=localhost'
openssl x509 -req -in store/server.csr -CA store/ca.crt -CAkey store/ca.key -CAcreateserial -out store/server.crt -days 3650 -sha256 -extfile store/server.ext
openssl x509 -noout -text -in store/server.crt
cp store/server.crt server-cert.pem
cp store/server.key server-key.pem

echo ================================
echo CLIENT
echo ================================
openssl req -new -nodes -out store/client.csr -newkey rsa:4096 -keyout store/client.key -subj '/CN=client'
openssl x509 -req -in store/client.csr -CA store/ca.crt -CAkey store/ca.key -CAcreateserial -out store/client.crt -days 3650 -sha256
openssl x509 -noout -text -in store/client.crt
cp store/client.crt client-cert.pem
cp store/client.key client-key.pem

rm -rf store
