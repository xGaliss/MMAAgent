import sqlite3
import sys
from pathlib import Path


DEFAULT_DB_PATH = (
    Path(__file__).resolve().parents[1]
    / "MMAAgent.Web"
    / "Assets"
    / "Database"
    / "MMA_Agent.db"
)


def main() -> int:
    db_path = parse_args(sys.argv[1:])
    if db_path is None:
        return 1

    db_path = db_path.resolve()
    if not db_path.exists():
        print(f"Database not found: {db_path}", file=sys.stderr)
        return 1

    conn = sqlite3.connect(str(db_path))
    try:
        errors: list[str] = []
        warnings: list[str] = []

        validate_schema(conn, errors, warnings)
        if not errors:
            validate_country_culture_coverage(conn, errors)
            validate_culture_name_pools(conn, errors)
            validate_country_override_tables(conn, errors, warnings)

        print(f"Validating country/culture data: {db_path}")
        print()

        if not errors and not warnings:
            print("OK: no issues found.")
            return 0

        if errors:
            print("Errors:")
            for error in errors:
                print(f"  - {error}")
            print()

        if warnings:
            print("Warnings:")
            for warning in warnings:
                print(f"  - {warning}")
            print()

        print(f"Summary: {len(errors)} error(s), {len(warnings)} warning(s).")
        return 1 if errors else 0
    finally:
        conn.close()


def parse_args(args: list[str]) -> Path | None:
    if not args:
        return DEFAULT_DB_PATH

    if len(args) == 2 and args[0] == "--db":
        return Path(args[1])

    print("Usage: python MMAAgent.Tools/validate_country_data.py [--db <path>]", file=sys.stderr)
    return None


def validate_schema(conn: sqlite3.Connection, errors: list[str], warnings: list[str]) -> None:
    required_tables = [
        "Countries",
        "CountryCultureWeights",
        "FirstNames",
        "FirstNameGroups",
        "FirstNameCountries",
        "LastNames",
        "LastNameGroups",
        "LastNameCountries",
        "Fighters",
    ]

    existing_tables = {
        row[0]
        for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table'")
    }

    for table in required_tables:
        if table not in existing_tables:
            errors.append(f"Missing required table: {table}")

    fighter_columns = {
        row[1]
        for row in conn.execute("PRAGMA table_info(Fighters)")
    }
    if "CulturalGroup" not in fighter_columns:
        errors.append("Missing required column: Fighters.CulturalGroup")

    index_exists = conn.execute(
        "SELECT 1 FROM sqlite_master WHERE type='index' AND name='UX_CountryCultureWeights_Country_Culture' LIMIT 1"
    ).fetchone()
    if not index_exists:
        warnings.append("Missing unique index UX_CountryCultureWeights_Country_Culture on CountryCultureWeights.")


def validate_country_culture_coverage(conn: sqlite3.Connection, errors: list[str]) -> None:
    rows = conn.execute(
        """
        SELECT
            c.Name,
            c.CulturalGroup,
            c.FighterSpawnWeight,
            COUNT(ccw.Id) AS CultureRows,
            COALESCE(SUM(ccw.Weight), 0) AS TotalCultureWeight
        FROM Countries c
        LEFT JOIN CountryCultureWeights ccw ON ccw.CountryId = c.Id
        GROUP BY c.Id, c.Name, c.CulturalGroup, c.FighterSpawnWeight
        ORDER BY c.Id
        """
    ).fetchall()

    for country_name, legacy_culture, spawn_weight, culture_rows, total_weight in rows:
        if spawn_weight > 0 and culture_rows == 0:
            errors.append(
                f"Country '{country_name}' has FighterSpawnWeight > 0 but no CountryCultureWeights rows."
            )
            continue

        if spawn_weight > 0 and total_weight <= 0:
            errors.append(f"Country '{country_name}' has non-positive total culture weight.")

        if culture_rows == 0 and not (legacy_culture or "").strip():
            errors.append(
                f"Country '{country_name}' has no CountryCultureWeights and no legacy CulturalGroup fallback."
            )


def validate_culture_name_pools(conn: sqlite3.Connection, errors: list[str]) -> None:
    cultures = [
        row[0]
        for row in conn.execute(
            "SELECT DISTINCT CulturalGroup FROM CountryCultureWeights WHERE TRIM(CulturalGroup) <> '' ORDER BY CulturalGroup"
        )
    ]

    for culture in cultures:
        first_count = scalar_int(
            conn,
            "SELECT COUNT(*) FROM FirstNameGroups WHERE CulturalGroup = ? AND Weight > 0",
            (culture,),
        )
        last_count = scalar_int(
            conn,
            "SELECT COUNT(*) FROM LastNameGroups WHERE CulturalGroup = ? AND Weight > 0",
            (culture,),
        )

        if first_count == 0:
            errors.append(f"Culture '{culture}' has no positive-weight first names in FirstNameGroups.")

        if last_count == 0:
            errors.append(f"Culture '{culture}' has no positive-weight last names in LastNameGroups.")


def validate_country_override_tables(
    conn: sqlite3.Connection,
    errors: list[str],
    warnings: list[str],
) -> None:
    orphan_first = scalar_int(
        conn,
        """
        SELECT COUNT(*)
        FROM FirstNameCountries fnc
        LEFT JOIN Countries c ON c.Id = fnc.CountryId
        LEFT JOIN FirstNames fn ON fn.Id = fnc.FirstNameId
        WHERE c.Id IS NULL OR fn.Id IS NULL
        """,
    )
    orphan_last = scalar_int(
        conn,
        """
        SELECT COUNT(*)
        FROM LastNameCountries lnc
        LEFT JOIN Countries c ON c.Id = lnc.CountryId
        LEFT JOIN LastNames ln ON ln.Id = lnc.LastNameId
        WHERE c.Id IS NULL OR ln.Id IS NULL
        """,
    )

    if orphan_first > 0:
        errors.append(f"FirstNameCountries contains {orphan_first} orphan override rows.")

    if orphan_last > 0:
        errors.append(f"LastNameCountries contains {orphan_last} orphan override rows.")

    non_positive_first = scalar_int(conn, "SELECT COUNT(*) FROM FirstNameCountries WHERE Weight <= 0")
    non_positive_last = scalar_int(conn, "SELECT COUNT(*) FROM LastNameCountries WHERE Weight <= 0")

    if non_positive_first > 0:
        warnings.append(f"FirstNameCountries contains {non_positive_first} non-positive override rows.")

    if non_positive_last > 0:
        warnings.append(f"LastNameCountries contains {non_positive_last} non-positive override rows.")


def scalar_int(conn: sqlite3.Connection, sql: str, params: tuple = ()) -> int:
    return int(conn.execute(sql, params).fetchone()[0])


if __name__ == "__main__":
    raise SystemExit(main())
