#!/bin/sh
# Debezium PostgreSQL connector kaydı
# Bu script, Kafka Connect health check'i geçtikten sonra
# connector-init servisi tarafından otomatik olarak çalıştırılır.

CONNECTOR_NAME="outbox-connector"
CONNECT_URL="http://kafka-connect:8083"

# Connector config JSON'ı
# Faz 2'de table.include.list ve outbox SMT ayarları doldurulacak.
CONFIG='{
  "name": "'"${CONNECTOR_NAME}"'",
  "config": {
    "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
    "database.hostname": "postgres-orders",
    "database.port": "5432",
    "database.user": "order_user",
    "database.password": "order_pass",
    "database.dbname": "orders_db",
    "topic.prefix": "cdc",
    "plugin.name": "pgoutput"
  }
}'

echo "Registering Debezium connector: ${CONNECTOR_NAME}"

# Connector zaten kayıtlı mı kontrol et, yoksa oluştur
STATUS=$(curl -s -o /dev/null -w "%{http_code}" "${CONNECT_URL}/connectors/${CONNECTOR_NAME}")
if [ "$STATUS" = "404" ]; then
  curl -s -X POST "${CONNECT_URL}/connectors" \
    -H "Content-Type: application/json" \
    -d "${CONFIG}" | echo "Connector registered successfully"
elif [ "$STATUS" = "200" ]; then
  echo "Connector already registered, updating config"
  curl -s -X PUT "${CONNECT_URL}/connectors/${CONNECTOR_NAME}/config" \
    -H "Content-Type: application/json" \
    -d "$(echo "${CONFIG}" | jq -r '.config')" | echo "Connector config updated"
else
  echo "Unexpected status: ${STATUS}"
  exit 1
fi

echo "Debezium connector registration complete"
