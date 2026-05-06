create schema if not exists mma_agent;

create table if not exists mma_agent.save_records (
    save_id text primary key,
    display_name text not null,
    storage_kind text not null,
    storage_locator text not null,
    lifecycle_state text not null,
    template_source text not null,
    backend_instance text null,
    file_name text not null,
    local_path text null,
    created_utc timestamptz not null,
    last_opened_utc timestamptz not null,
    last_write_time_utc timestamptz not null,
    file_size_bytes bigint not null
);

create table if not exists mma_agent.save_ownership (
    save_id text not null references mma_agent.save_records(save_id) on delete cascade,
    owner_user_id text not null,
    is_primary boolean not null default true,
    assigned_utc timestamptz not null default timezone('utc', now()),
    primary key (save_id, owner_user_id)
);

create unique index if not exists ix_save_ownership_primary
    on mma_agent.save_ownership(save_id)
    where is_primary = true;

create index if not exists ix_save_ownership_owner_user_id
    on mma_agent.save_ownership(owner_user_id);

create index if not exists ix_save_records_local_path
    on mma_agent.save_records(local_path);

create table if not exists mma_agent.save_snapshots (
    save_id text primary key references mma_agent.save_records(save_id) on delete cascade,
    content bytea not null,
    content_sha256 text not null,
    content_size_bytes bigint not null,
    uploaded_utc timestamptz not null,
    source_local_path text null,
    sync_reason text null,
    revision integer not null default 1
);
