ALTER TABLE `reeks`
	ADD COLUMN `FrenoyTeamId` VARCHAR(10) NULL AFTER `LinkID`,
	ADD COLUMN `FrenoyDivisionId` INT NULL AFTER `FrenoyTeamId`;

