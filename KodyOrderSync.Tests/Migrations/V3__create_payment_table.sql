-- Flyway Migration: V3__create_payment_table.sql

CREATE TABLE `payment`
(
    `payment_id`      int(11) unsigned NOT NULL AUTO_INCREMENT,
    `order_head_id`   int(11)        DEFAULT NULL,
    `check_id`        int(11)        DEFAULT NULL,
    `tender_media_id` int(11)        DEFAULT NULL,
    `total`           decimal(11, 2) DEFAULT NULL,
    `employee_id`     int(11)        DEFAULT NULL,
    `remark`          varchar(30)    DEFAULT NULL,
    `payment_time`    datetime       DEFAULT NULL,
    `pos_device_id`   int(11)        DEFAULT NULL,
    `rvc_center_id`   int(11)        DEFAULT NULL,
    `order_detail_id` int(11)        DEFAULT NULL,
    `consume_id`      int(11)        DEFAULT NULL,
    `ticket_id`       int(11)        DEFAULT NULL,
    `wechat_id`       varchar(32)    DEFAULT NULL,
    PRIMARY KEY (`payment_id`),
    KEY `idx_headcheck` (`order_head_id`, `check_id`) USING BTREE
) ENGINE = InnoDB
  AUTO_INCREMENT = 4
  DEFAULT CHARSET=utf8 COMMENT='Payment transactions for order payments';