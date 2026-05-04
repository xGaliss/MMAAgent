-- Canonical country/culture/name seed for the web template DB.
-- CountryCultureWeights defines which cultures exist in a country and with what weight.
-- FirstNameGroups / LastNameGroups remain the main cultural pools.
-- FirstNameCountries / LastNameCountries stay optional flavor overrides, not the primary source.
-- Fighters.CulturalGroup is expected to exist in the template DB itself.
-- Runtime schema preparation only keeps old saves safe.

BEGIN TRANSACTION;

CREATE TABLE IF NOT EXISTS CountryCultureWeights
(
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CountryId INTEGER NOT NULL,
    CulturalGroup TEXT NOT NULL,
    Weight INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(CountryId) REFERENCES Countries(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_CountryCultureWeights_CountryId
    ON CountryCultureWeights(CountryId);

CREATE UNIQUE INDEX IF NOT EXISTS UX_CountryCultureWeights_Country_Culture
    ON CountryCultureWeights(CountryId, CulturalGroup);

-- Additional countries so the generator can scale beyond the original 10.
INSERT INTO Countries (Name, MmaLevel, WrestlingBias, StrikingBias, GrapplingBias, FighterSpawnWeight, CulturalGroup)
SELECT 'Colombia', 68, -2, 8, 0, 28, 'Hispanic'
WHERE NOT EXISTS (SELECT 1 FROM Countries WHERE Name = 'Colombia');

INSERT INTO Countries (Name, MmaLevel, WrestlingBias, StrikingBias, GrapplingBias, FighterSpawnWeight, CulturalGroup)
SELECT 'Chile', 61, 0, 5, 0, 18, 'Hispanic'
WHERE NOT EXISTS (SELECT 1 FROM Countries WHERE Name = 'Chile');

INSERT INTO Countries (Name, MmaLevel, WrestlingBias, StrikingBias, GrapplingBias, FighterSpawnWeight, CulturalGroup)
SELECT 'Peru', 60, -1, 4, 1, 20, 'Hispanic'
WHERE NOT EXISTS (SELECT 1 FROM Countries WHERE Name = 'Peru');

INSERT INTO Countries (Name, MmaLevel, WrestlingBias, StrikingBias, GrapplingBias, FighterSpawnWeight, CulturalGroup)
SELECT 'Netherlands', 74, 0, 12, 0, 26, 'Germanic'
WHERE NOT EXISTS (SELECT 1 FROM Countries WHERE Name = 'Netherlands');

INSERT INTO Countries (Name, MmaLevel, WrestlingBias, StrikingBias, GrapplingBias, FighterSpawnWeight, CulturalGroup)
SELECT 'Poland', 69, 12, 1, 4, 26, 'Slavic'
WHERE NOT EXISTS (SELECT 1 FROM Countries WHERE Name = 'Poland');

INSERT INTO Countries (Name, MmaLevel, WrestlingBias, StrikingBias, GrapplingBias, FighterSpawnWeight, CulturalGroup)
SELECT 'Sweden', 67, 3, 7, 0, 18, 'Nordic'
WHERE NOT EXISTS (SELECT 1 FROM Countries WHERE Name = 'Sweden');

INSERT INTO Countries (Name, MmaLevel, WrestlingBias, StrikingBias, GrapplingBias, FighterSpawnWeight, CulturalGroup)
SELECT 'Nigeria', 71, 4, 12, 0, 22, 'African'
WHERE NOT EXISTS (SELECT 1 FROM Countries WHERE Name = 'Nigeria');

INSERT INTO Countries (Name, MmaLevel, WrestlingBias, StrikingBias, GrapplingBias, FighterSpawnWeight, CulturalGroup)
SELECT 'Australia', 71, 2, 8, 0, 24, 'Anglo'
WHERE NOT EXISTS (SELECT 1 FROM Countries WHERE Name = 'Australia');

INSERT INTO Countries (Name, MmaLevel, WrestlingBias, StrikingBias, GrapplingBias, FighterSpawnWeight, CulturalGroup)
SELECT 'South Korea', 69, 2, 8, 4, 20, 'Korean'
WHERE NOT EXISTS (SELECT 1 FROM Countries WHERE Name = 'South Korea');

INSERT INTO Countries (Name, MmaLevel, WrestlingBias, StrikingBias, GrapplingBias, FighterSpawnWeight, CulturalGroup)
SELECT 'China', 65, 3, 9, 1, 22, 'Chinese'
WHERE NOT EXISTS (SELECT 1 FROM Countries WHERE Name = 'China');

-- First names for missing cultural pools.
INSERT INTO FirstNames (Name, Gender)
SELECT 'Thiago', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Thiago' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Akira', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Akira' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Ren', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Ren' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Daichi', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Daichi' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Antoine', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Antoine' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Jeroen', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Jeroen' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Bas', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Bas' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Daan', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Daan' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Sven', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Sven' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Erik', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Erik' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Lars', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Lars' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Nils', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Nils' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Viktor', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Viktor' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Kofi', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Kofi' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Musa', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Musa' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Chidi', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Chidi' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Tunde', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Tunde' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Minho', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Minho' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Joon', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Joon' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Hyun', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Hyun' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Seong', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Seong' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Wei', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Wei' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Jian', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Jian' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Ming', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Ming' AND Gender = 'M');

INSERT INTO FirstNames (Name, Gender)
SELECT 'Hao', 'M'
WHERE NOT EXISTS (SELECT 1 FROM FirstNames WHERE Name = 'Hao' AND Gender = 'M');

-- Last names for missing cultural pools.
INSERT INTO LastNames (Name)
SELECT 'Nakamura'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Nakamura');

INSERT INTO LastNames (Name)
SELECT 'Ito'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Ito');

INSERT INTO LastNames (Name)
SELECT 'De Jong'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'De Jong');

INSERT INTO LastNames (Name)
SELECT 'Van Dijk'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Van Dijk');

INSERT INTO LastNames (Name)
SELECT 'Jansen'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Jansen');

INSERT INTO LastNames (Name)
SELECT 'Bakker'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Bakker');

INSERT INTO LastNames (Name)
SELECT 'Andersson'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Andersson');

INSERT INTO LastNames (Name)
SELECT 'Karlsson'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Karlsson');

INSERT INTO LastNames (Name)
SELECT 'Lindberg'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Lindberg');

INSERT INTO LastNames (Name)
SELECT 'Nystrom'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Nystrom');

INSERT INTO LastNames (Name)
SELECT 'Adeyemi'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Adeyemi');

INSERT INTO LastNames (Name)
SELECT 'Okafor'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Okafor');

INSERT INTO LastNames (Name)
SELECT 'Mensah'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Mensah');

INSERT INTO LastNames (Name)
SELECT 'Balogun'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Balogun');

INSERT INTO LastNames (Name)
SELECT 'Kim'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Kim');

INSERT INTO LastNames (Name)
SELECT 'Park'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Park');

INSERT INTO LastNames (Name)
SELECT 'Lee'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Lee');

INSERT INTO LastNames (Name)
SELECT 'Choi'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Choi');

INSERT INTO LastNames (Name)
SELECT 'Wang'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Wang');

INSERT INTO LastNames (Name)
SELECT 'Li'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Li');

INSERT INTO LastNames (Name)
SELECT 'Zhang'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Zhang');

INSERT INTO LastNames (Name)
SELECT 'Chen'
WHERE NOT EXISTS (SELECT 1 FROM LastNames WHERE Name = 'Chen');

-- Cultural first-name pools.
INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Lusophone', 90
FROM FirstNames fn
WHERE fn.Name = 'Joao'
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Lusophone');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Lusophone', 85
FROM FirstNames fn
WHERE fn.Name = 'Pedro'
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Lusophone');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Lusophone', 88
FROM FirstNames fn
WHERE fn.Name = 'Rafael'
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Lusophone');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Lusophone', 80
FROM FirstNames fn
WHERE fn.Name = 'Bruno'
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Lusophone');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Lusophone', 78
FROM FirstNames fn
WHERE fn.Name = 'Thiago'
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Lusophone');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Japanese', 88
FROM FirstNames fn
WHERE fn.Name = 'Kenji'
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Japanese');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Japanese', 82
FROM FirstNames fn
WHERE fn.Name = 'Hiro'
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Japanese');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Japanese', 78
FROM FirstNames fn
WHERE fn.Name IN ('Akira', 'Ren', 'Daichi')
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Japanese');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Francophone', 85
FROM FirstNames fn
WHERE fn.Name IN ('Jean', 'Pierre', 'Louis', 'Nicolas')
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Francophone');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Francophone', 76
FROM FirstNames fn
WHERE fn.Name = 'Antoine'
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Francophone');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Germanic', 78
FROM FirstNames fn
WHERE fn.Name IN ('Jeroen', 'Bas', 'Daan', 'Sven')
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Germanic');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Nordic', 80
FROM FirstNames fn
WHERE fn.Name IN ('Erik', 'Lars', 'Nils', 'Viktor')
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Nordic');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'African', 80
FROM FirstNames fn
WHERE fn.Name IN ('Kofi', 'Musa', 'Chidi', 'Tunde')
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'African');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Korean', 82
FROM FirstNames fn
WHERE fn.Name IN ('Minho', 'Joon', 'Hyun', 'Seong')
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Korean');

INSERT INTO FirstNameGroups (FirstNameId, CulturalGroup, Weight)
SELECT fn.Id, 'Chinese', 82
FROM FirstNames fn
WHERE fn.Name IN ('Wei', 'Jian', 'Ming', 'Hao')
  AND NOT EXISTS (SELECT 1 FROM FirstNameGroups WHERE FirstNameId = fn.Id AND CulturalGroup = 'Chinese');

-- Cultural last-name pools.
INSERT INTO LastNameGroups (LastNameId, CulturalGroup, Weight)
SELECT ln.Id, 'Japanese', 90
FROM LastNames ln
WHERE ln.Name IN ('Sato', 'Tanaka')
  AND NOT EXISTS (SELECT 1 FROM LastNameGroups WHERE LastNameId = ln.Id AND CulturalGroup = 'Japanese');

INSERT INTO LastNameGroups (LastNameId, CulturalGroup, Weight)
SELECT ln.Id, 'Japanese', 82
FROM LastNames ln
WHERE ln.Name IN ('Nakamura', 'Ito')
  AND NOT EXISTS (SELECT 1 FROM LastNameGroups WHERE LastNameId = ln.Id AND CulturalGroup = 'Japanese');

INSERT INTO LastNameGroups (LastNameId, CulturalGroup, Weight)
SELECT ln.Id, 'Germanic', 84
FROM LastNames ln
WHERE ln.Name IN ('De Jong', 'Van Dijk', 'Jansen', 'Bakker')
  AND NOT EXISTS (SELECT 1 FROM LastNameGroups WHERE LastNameId = ln.Id AND CulturalGroup = 'Germanic');

INSERT INTO LastNameGroups (LastNameId, CulturalGroup, Weight)
SELECT ln.Id, 'Nordic', 84
FROM LastNames ln
WHERE ln.Name IN ('Andersson', 'Karlsson', 'Lindberg', 'Nystrom')
  AND NOT EXISTS (SELECT 1 FROM LastNameGroups WHERE LastNameId = ln.Id AND CulturalGroup = 'Nordic');

INSERT INTO LastNameGroups (LastNameId, CulturalGroup, Weight)
SELECT ln.Id, 'African', 82
FROM LastNames ln
WHERE ln.Name IN ('Adeyemi', 'Okafor', 'Mensah', 'Balogun')
  AND NOT EXISTS (SELECT 1 FROM LastNameGroups WHERE LastNameId = ln.Id AND CulturalGroup = 'African');

INSERT INTO LastNameGroups (LastNameId, CulturalGroup, Weight)
SELECT ln.Id, 'Korean', 88
FROM LastNames ln
WHERE ln.Name IN ('Kim', 'Park', 'Lee', 'Choi')
  AND NOT EXISTS (SELECT 1 FROM LastNameGroups WHERE LastNameId = ln.Id AND CulturalGroup = 'Korean');

INSERT INTO LastNameGroups (LastNameId, CulturalGroup, Weight)
SELECT ln.Id, 'Chinese', 88
FROM LastNames ln
WHERE ln.Name IN ('Wang', 'Li', 'Zhang', 'Chen')
  AND NOT EXISTS (SELECT 1 FROM LastNameGroups WHERE LastNameId = ln.Id AND CulturalGroup = 'Chinese');

-- Country to culture weights.
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 70 FROM Countries c
WHERE c.Name = 'USA'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 20 FROM Countries c
WHERE c.Name = 'USA'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Slavic', 5 FROM Countries c
WHERE c.Name = 'USA'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Slavic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Lusophone', 3 FROM Countries c
WHERE c.Name = 'USA'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Lusophone');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Japanese', 2 FROM Countries c
WHERE c.Name = 'USA'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Japanese');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Lusophone', 90 FROM Countries c
WHERE c.Name = 'Brazil'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Lusophone');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Japanese', 5 FROM Countries c
WHERE c.Name = 'Brazil'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Japanese');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 5 FROM Countries c
WHERE c.Name = 'Brazil'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Slavic', 95 FROM Countries c
WHERE c.Name = 'Russia'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Slavic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 5 FROM Countries c
WHERE c.Name = 'Russia'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 98 FROM Countries c
WHERE c.Name = 'Mexico'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 2 FROM Countries c
WHERE c.Name = 'Mexico'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 95 FROM Countries c
WHERE c.Name = 'Spain'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 5 FROM Countries c
WHERE c.Name = 'Spain'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 92 FROM Countries c
WHERE c.Name = 'Argentina'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 3 FROM Countries c
WHERE c.Name = 'Argentina'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Slavic', 5 FROM Countries c
WHERE c.Name = 'Argentina'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Slavic');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Japanese', 95 FROM Countries c
WHERE c.Name = 'Japan'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Japanese');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 5 FROM Countries c
WHERE c.Name = 'Japan'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Francophone', 90 FROM Countries c
WHERE c.Name = 'France'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Francophone');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 5 FROM Countries c
WHERE c.Name = 'France'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 5 FROM Countries c
WHERE c.Name = 'France'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 90 FROM Countries c
WHERE c.Name = 'UK'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 3 FROM Countries c
WHERE c.Name = 'UK'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Slavic', 2 FROM Countries c
WHERE c.Name = 'UK'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Slavic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'African', 5 FROM Countries c
WHERE c.Name = 'UK'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'African');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 70 FROM Countries c
WHERE c.Name = 'Canada'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Francophone', 25 FROM Countries c
WHERE c.Name = 'Canada'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Francophone');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 5 FROM Countries c
WHERE c.Name = 'Canada'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 96 FROM Countries c
WHERE c.Name = 'Colombia'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 2 FROM Countries c
WHERE c.Name = 'Colombia'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'African', 2 FROM Countries c
WHERE c.Name = 'Colombia'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'African');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 96 FROM Countries c
WHERE c.Name = 'Chile'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 4 FROM Countries c
WHERE c.Name = 'Chile'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 97 FROM Countries c
WHERE c.Name = 'Peru'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 3 FROM Countries c
WHERE c.Name = 'Peru'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Germanic', 85 FROM Countries c
WHERE c.Name = 'Netherlands'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Germanic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 10 FROM Countries c
WHERE c.Name = 'Netherlands'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 5 FROM Countries c
WHERE c.Name = 'Netherlands'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Slavic', 95 FROM Countries c
WHERE c.Name = 'Poland'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Slavic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 5 FROM Countries c
WHERE c.Name = 'Poland'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Nordic', 92 FROM Countries c
WHERE c.Name = 'Sweden'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Nordic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 8 FROM Countries c
WHERE c.Name = 'Sweden'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'African', 94 FROM Countries c
WHERE c.Name = 'Nigeria'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'African');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 6 FROM Countries c
WHERE c.Name = 'Nigeria'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 92 FROM Countries c
WHERE c.Name = 'Australia'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Hispanic', 3 FROM Countries c
WHERE c.Name = 'Australia'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Hispanic');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Chinese', 2 FROM Countries c
WHERE c.Name = 'Australia'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Chinese');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Japanese', 1 FROM Countries c
WHERE c.Name = 'Australia'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Japanese');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Korean', 2 FROM Countries c
WHERE c.Name = 'Australia'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Korean');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Korean', 95 FROM Countries c
WHERE c.Name = 'South Korea'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Korean');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 5 FROM Countries c
WHERE c.Name = 'South Korea'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');

INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Chinese', 96 FROM Countries c
WHERE c.Name = 'China'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Chinese');
INSERT INTO CountryCultureWeights (CountryId, CulturalGroup, Weight)
SELECT c.Id, 'Anglo', 4 FROM Countries c
WHERE c.Name = 'China'
  AND NOT EXISTS (SELECT 1 FROM CountryCultureWeights WHERE CountryId = c.Id AND CulturalGroup = 'Anglo');

COMMIT;
