#!/bin/bash
set -e

echo "=== Melodee Container Startup ==="

# Check if running as non-root (for rootless podman)
if [ "$(id -u)" -ne 0 ]; then
    echo "Running as non-root user (UID=$(id -u), GID=$(id -g))"
    echo "Skipping permission fixes (rootless container mode)"
    
    # Create required directories with current user
    # These will be created in the volumes with the current user's ownership
    echo "Creating required directories..."
    mkdir -p /app/storage/_search-engines/musicbrainz
    mkdir -p \
        /app/podcasts \
        /app/themes \
        /app/inbound \
        /app/staging \
        /app/user-images \
        /app/playlists \
        /app/templates \
        /app/Logs
    mkdir -p ~/.aspnet/DataProtection-Keys
    
    # Ensure we can write to these directories
    # In rootless mode, these are mounted volumes and should already be writable
    # But create subdirectories as needed
    if touch /app/storage/.melodee_test 2>/dev/null; then
        rm -f /app/storage/.melodee_test
    else
        echo "WARNING: Cannot write to /app/storage - volume may have permission issues"
    fi
    
    # Wait for database
    echo "Waiting for database..."
    until pg_isready -h melodee-db -p 5432 -U melodeeuser -d melodeedb -q; do
        echo "Database not ready, retrying in 2 seconds..."
        sleep 2
    done
    echo "Database is ready!"
    
    # Run migrations
    echo "Running database migrations..."
    if [ -f /app/efbundle ]; then
        /app/efbundle --connection "Host=melodee-db;Port=5432;Database=melodeedb;Username=melodeeuser;Password=${DB_PASSWORD};SSL Mode=Disable"
        echo "Migrations completed!"
    else
        echo "Warning: Migration bundle not found, skipping migrations"
    fi
    
    # Start application directly (no user switch needed)
    echo "Starting Melodee server as UID=$(id -u)..."
    exec dotnet server.dll
else
    # Running as root (traditional docker or rootful podman)
    echo "Running as root - fixing volume permissions..."
    
    # Fix volume permissions (runs as root initially)
    echo "Fixing volume permissions..."
    chown -R melodee:melodee \
        /app/storage \
        /app/podcasts \
        /app/themes \
        /app/inbound \
        /app/staging \
        /app/user-images \
        /app/playlists \
        /app/templates \
        /app/Logs \
        2>/dev/null || true
    chmod -R 755 \
        /app/storage \
        /app/podcasts \
        /app/themes \
        /app/inbound \
        /app/staging \
        /app/user-images \
        /app/playlists \
        /app/templates \
        2>/dev/null || true
    
    # Fix DataProtection keys directory permissions
    echo "Fixing DataProtection keys directory permissions..."
    mkdir -p /home/melodee/.aspnet/DataProtection-Keys
    chown -R melodee:melodee /home/melodee/.aspnet
    chmod -R 755 /home/melodee/.aspnet
    
    # Create required subdirectories for search engines
    echo "Creating search engine directories..."
    mkdir -p /app/storage/_search-engines/musicbrainz
    chown -R melodee:melodee /app/storage/_search-engines
    chmod -R 755 /app/storage/_search-engines
    
    # Wait for database to be ready
    echo "Waiting for database..."
    until pg_isready -h melodee-db -p 5432 -U melodeeuser -d melodeedb -q; do
        echo "Database not ready, retrying in 2 seconds..."
        sleep 2
    done
    echo "Database is ready!"
    
    # Run database migrations using the pre-built bundle
    echo "Running database migrations..."
    if [ -f /app/efbundle ]; then
        /app/efbundle --connection "Host=melodee-db;Port=5432;Database=melodeedb;Username=melodeeuser;Password=${DB_PASSWORD};SSL Mode=Disable"
        echo "Migrations completed!"
    else
        echo "Warning: Migration bundle not found, skipping migrations"
    fi
    
    # Drop privileges and replace PID 1 with the application process.
    echo "Starting Melodee server as melodee user..."
    cd /app
    exec /usr/bin/setpriv \
        --reuid=melodee \
        --regid=melodee \
        --init-groups \
        dotnet server.dll
fi
