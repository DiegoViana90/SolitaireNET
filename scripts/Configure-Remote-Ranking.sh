#!/usr/bin/env bash
set -euo pipefail

DB_NAME="${DB_NAME:-solitairenet}"
DB_USER="${DB_USER:-solitairenet}"
FIREBASE_PROJECT_ID="${FIREBASE_PROJECT_ID:-paciencianet}"
PASSWORD_FILE="${PASSWORD_FILE:-/etc/solitairenet/postgres-ranking-password}"
DROPIN_DIR="${DROPIN_DIR:-/etc/systemd/system/solitairenet-api.service.d}"
DROPIN_FILE="$DROPIN_DIR/auth-ranking.conf"

install -m 700 -d "$(dirname "$PASSWORD_FILE")"

if [ ! -s "$PASSWORD_FILE" ]; then
  umask 077
  openssl rand -base64 36 > "$PASSWORD_FILE"
fi

chmod 600 "$PASSWORD_FILE"

DB_PASSWORD="$(cat "$PASSWORD_FILE")"
PASSWORD_SQL="${DB_PASSWORD//\'/\'\'}"

sudo -u postgres psql -v ON_ERROR_STOP=1 <<SQL
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '$DB_USER') THEN
    CREATE ROLE $DB_USER LOGIN PASSWORD '$PASSWORD_SQL';
  ELSE
    ALTER ROLE $DB_USER LOGIN PASSWORD '$PASSWORD_SQL';
  END IF;
END
\$\$;

SELECT 'CREATE DATABASE $DB_NAME OWNER $DB_USER'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = '$DB_NAME')\gexec

GRANT ALL PRIVILEGES ON DATABASE $DB_NAME TO $DB_USER;
\c $DB_NAME
GRANT ALL ON SCHEMA public TO $DB_USER;
SQL

install -d "$DROPIN_DIR"
cat > "$DROPIN_FILE" <<EOF
[Service]
Environment=Firebase__ProjectId=$FIREBASE_PROJECT_ID
Environment=Ranking__ConnectionString=Host=127.0.0.1;Port=5432;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASSWORD
EOF

chmod 600 "$DROPIN_FILE"
systemctl daemon-reload

sudo -u postgres psql -d "$DB_NAME" -c "SELECT current_database() AS database;"
