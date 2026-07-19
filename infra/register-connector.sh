#!/bin/sh
# Debezium PostgreSQL connector kaydı
# connector-init servisi tarafından otomatik çalıştırılır.
# Outbox Event Router SMT ile outbox_messages tablosu -> order-created topic.

CONNECTOR_NAME="outbox-connector"
CONNECT_URL="http://kafka-connect:8083"

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
    "plugin.name": "pgoutput",

    "table.include.list": "public.outbox_messages",

    "transforms": "outbox",
    "transforms.outbox.type": "io.debezium.transforms.outbox.EventRouter",

    "transforms.outbox.table.field.event.id": "id",
    "transforms.outbox.table.field.event.type": "event_type",
    "transforms.outbox.table.field.event.payload": "payload",
    "transforms.outbox.table.field.event.key": "id",
    "transforms.outbox.table.field.event.aggregate.type": "event_type",

    "transforms.outbox.route.by.field": "event_type",
    "transforms.outbox.route.topic.replacement": "order-created",

    "value.converter": "org.apache.kafka.connect.json.JsonConverter",
    "value.converter.schemas.enable": "false",
    "key.converter": "org.apache.kafka.connect.json.JsonConverter",
    "key.converter.schemas.enable": "false"
  }
}'

echo "Registering Debezium connector: ${CONNECTOR_NAME}"

STATUS=$(curl -s -o /dev/null -w "%{http_code}" "${CONNECT_URL}/connectors/${CONNECTOR_NAME}")
if [ "$STATUS" = "200" ]; then
  echo "Connector already registered, deleting and recreating"
  curl -s -X DELETE "${CONNECT_URL}/connectors/${CONNECTOR_NAME}" > /dev/null
  sleep 2
fi

echo "Creating connector: ${CONNECTOR_NAME}"
curl -s -X POST "${CONNECT_URL}/connectors" \
  -H "Content-Type: application/json" \
  -d "${CONFIG}"

echo "Debezium connector registration complete"
