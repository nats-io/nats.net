#!/usr/bin/env bash

set -e

# -subj values may fail on Windows if running under MSYSGIT
# Make sure to disable POSIX-to-Windows path conversion e.g.
# export MSYS_NO_PATHCONV=1
#

function create_child_cert {
    signing_cert_name=$1
    child_cert_name=$2
    is_ca=$3

    openssl req -new -nodes -out "store/$child_cert_name.csr" -newkey rsa:4096 -keyout "store/$child_cert_name.key" -subj "/CN=$child_cert_name"
    if [[ "$is_ca" == "true" ]]; then
        # Use CA extensions for intermediate CA certificates
        cat <<EOF > "store/$child_cert_name.ext"
basicConstraints=CA:TRUE
keyUsage = digitalSignature, keyCertSign, cRLSign
EOF
        openssl x509 -req -in "store/$child_cert_name.csr" -CA "store/$signing_cert_name.crt" -CAkey "store/$signing_cert_name.key" -CAcreateserial -out "store/$child_cert_name.crt" -days 3650 -sha256 -extfile "store/$child_cert_name.ext"
    else
        # For end-entity certificates (non-CAs)
        openssl x509 -req -in "store/$child_cert_name.csr" -CA "store/$signing_cert_name.crt" -CAkey "store/$signing_cert_name.key" -CAcreateserial -out "store/$child_cert_name.crt" -days 3650 -sha256
    fi
    openssl x509 -noout -text -in "store/$child_cert_name.crt"
    cp "store/$child_cert_name.crt" "$child_cert_name-cert.pem"
    cp "store/$child_cert_name.key" "$child_cert_name-key.pem"
}

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
create_child_cert ca client

echo ================================
echo CLIENT WITH CHAIN
echo ================================
create_child_cert ca intermediate01 true
create_child_cert intermediate01 intermediate02 true
create_child_cert intermediate02 leafclient false
# create single cert file for chained client
cp store/leafclient.crt chainedclient-cert.pem
cp store/leafclient.key chainedclient-key.pem
cat store/intermediate02.crt >> chainedclient-cert.pem
cat store/intermediate01.crt >> chainedclient-cert.pem

rm -rf store

echo ================================
echo DONE
echo ================================
