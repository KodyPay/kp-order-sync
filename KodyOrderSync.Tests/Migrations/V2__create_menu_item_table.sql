-- Flyway Migration: V2__create_menu_item_table.sql

CREATE TABLE `menu_item`
(
    `item_id`               INT         NOT NULL COMMENT 'Item ID (can be tied to employee card if applicable)',
    `item_name1`            VARCHAR(20) NOT NULL COMMENT 'Primary item name (Unique)',
    `item_name2`            VARCHAR(20)  DEFAULT NULL COMMENT 'Secondary item name',
    `icon`                  VARCHAR(512) DEFAULT NULL COMMENT 'Path and filename of the item image',
    `slu_id`                INT          DEFAULT NULL COMMENT 'Second-level menu category ID (FK to descriptors_menu_item_slu)',
    `nlu`                   VARCHAR(20)  DEFAULT NULL COMMENT 'Quick order code, e.g. hstd = 红烧土豆',
    `class_id`              INT          DEFAULT NULL COMMENT 'Item class ID (FK to menu_item_class)',
    `print_class`           INT          DEFAULT NULL COMMENT 'Print group ID (FK to print_class)',
    `item_type`             INT          DEFAULT NULL COMMENT 'Item type: 0-normal, 1-condiment, 2-market price, 3-combo, 4-custom, 5-text, 6-service',
    `allow_condiment`       INT          DEFAULT NULL COMMENT 'Optional condiment group (FK to condiment_membership)',
    `required_condiment`    INT          DEFAULT NULL COMMENT 'Required condiment group (FK to condiment_membership)',
    `check_availability`    BIT          DEFAULT b'0' COMMENT 'Check stock for limited-sale item (0 = unlimited)',
    `no_access_mgr`         BIT          DEFAULT NULL COMMENT 'Custom condiment control flag',
    `major_group`           INT          DEFAULT 0 COMMENT 'Major report group (FK)',
    `family_group`          INT          DEFAULT 0 COMMENT 'Minor report group (FK)',

    -- Price Group 1
    `price_1`               FLOAT        DEFAULT 0 COMMENT 'Price 1',
    `take_out_price_1`      FLOAT        DEFAULT 0 COMMENT 'Takeout price 1 (0 = use dine-in price)',
    `cost_1`                FLOAT        DEFAULT 0 COMMENT 'Cost price 1',
    `unit_1`                VARCHAR(30)  DEFAULT NULL COMMENT 'Unit 1',
    `date_from_1`           DATETIME     DEFAULT NULL COMMENT 'Start date 1',
    `date_to_1`             DATETIME     DEFAULT NULL COMMENT 'End date 1',
    `surcharge_1`           FLOAT        DEFAULT 0 COMMENT 'Surcharge 1',
    `tare_weight_1`         FLOAT        DEFAULT NULL COMMENT 'Tare weight 1',

    -- Price Group 2
    `price_2`               FLOAT        DEFAULT 0,
    `take_out_price_2`      FLOAT        DEFAULT 0,
    `cost_2`                FLOAT        DEFAULT 0,
    `unit_2`                VARCHAR(30)  DEFAULT NULL,
    `date_from_2`           DATETIME     DEFAULT NULL,
    `date_to_2`             DATETIME     DEFAULT NULL,
    `surcharge_2`           FLOAT        DEFAULT 0,
    `tare_weight_2`         FLOAT        DEFAULT NULL,

    -- Price Group 3
    `price_3`               FLOAT        DEFAULT 0,
    `take_out_price_3`      FLOAT        DEFAULT 0,
    `cost_3`                FLOAT        DEFAULT 0,
    `unit_3`                VARCHAR(30)  DEFAULT NULL,
    `date_from_3`           DATETIME     DEFAULT NULL,
    `date_to_3`             DATETIME     DEFAULT NULL,
    `surcharge_3`           FLOAT        DEFAULT 0,
    `tare_weight_3`         FLOAT        DEFAULT NULL,

    -- Price Group 4
    `price_4`               FLOAT        DEFAULT 0,
    `take_out_price_4`      FLOAT        DEFAULT 0,
    `cost_4`                FLOAT        DEFAULT 0,
    `unit_4`                VARCHAR(30)  DEFAULT NULL,
    `date_from_4`           DATETIME     DEFAULT NULL,
    `date_to_4`             DATETIME     DEFAULT NULL,
    `surcharge_4`           FLOAT        DEFAULT 0,
    `tare_weight_4`         FLOAT        DEFAULT NULL,

    -- Price Group 5
    `price_5`               FLOAT        DEFAULT 0,
    `take_out_price_5`      FLOAT        DEFAULT 0,
    `cost_5`                FLOAT        DEFAULT 0,
    `unit_5`                VARCHAR(30)  DEFAULT NULL,
    `date_from_5`           DATETIME     DEFAULT NULL,
    `date_to_5`             DATETIME     DEFAULT NULL,
    `surcharge_5`           FLOAT        DEFAULT 0,
    `tare_weight_5`         FLOAT        DEFAULT NULL,

    `slu_priority`          INT          DEFAULT NULL COMMENT 'Display priority (higher = higher priority)',
    `period_class_id`       INT          DEFAULT NULL COMMENT 'Available time period group (FK to serving_period_class)',
    `rvc_class_id`          INT          DEFAULT NULL COMMENT 'Available RVC group (FK to serving_rvc_class)',
    `commission_type`       INT          DEFAULT 0 COMMENT 'Commission type: 0-none, 1-per item, 2-percentage',
    `commission_value`      FLOAT        DEFAULT NULL COMMENT 'Commission value',
    `ticket_class`          INT          DEFAULT 1 COMMENT 'Voucher group allowed',
    `tax_group`             INT          DEFAULT NULL COMMENT 'Tax group (FK)',
    `is_discount`           INT          DEFAULT NULL COMMENT 'Whether discountable: 0 = no, 9 = yes',
    `is_service`            INT          DEFAULT NULL COMMENT 'Whether service fee is applied: 0 = no, 9 = yes',
    `weight_entry_required` INT          DEFAULT NULL COMMENT '1 = weight entry required',
    `skiller_group_id`      INT          DEFAULT NULL COMMENT 'Default technician group (FK to employee_class)',

    PRIMARY KEY (`item_id`),
    UNIQUE KEY `uk_item_name1` (`item_name1`)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4 COMMENT ='Menu item table for defining individual dishes';