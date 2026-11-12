\set ON_ERROR_STOP on

SELECT 'CREATE ROLE inventory_app WITH LOGIN'
WHERE NOT EXISTS (
    SELECT 1 FROM pg_roles WHERE rolname = 'inventory_app'
)
\gexec

SELECT 'CREATE DATABASE inventory OWNER inventory_app'
WHERE NOT EXISTS (
    SELECT 1 FROM pg_database WHERE datname = 'inventory'
)
\gexec

SELECT datname AS database_name,
       pg_get_userbyid(datdba) AS owner
FROM pg_database
WHERE datname = 'inventory';