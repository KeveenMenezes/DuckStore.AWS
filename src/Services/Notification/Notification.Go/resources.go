package main

import (
	"context"
	"log"

	"github.com/aws/aws-sdk-go-v2/aws"
	"github.com/aws/aws-sdk-go-v2/service/dynamodb"
	dbtypes "github.com/aws/aws-sdk-go-v2/service/dynamodb/types"
	"github.com/aws/aws-sdk-go-v2/service/sqs"
)

// ensureResources creates the SQS queue and DynamoDB table if they don't exist.
// This is used in local development with LocalStack.
func ensureResources(ctx context.Context, sqsClient *sqs.Client, dynamoClient *dynamodb.Client, queueName, tableName string) error {
	if err := ensureQueue(ctx, sqsClient, queueName); err != nil {
		return err
	}
	return ensureTable(ctx, dynamoClient, tableName)
}

func ensureQueue(ctx context.Context, client *sqs.Client, queueName string) error {
	_, err := client.GetQueueUrl(ctx, &sqs.GetQueueUrlInput{
		QueueName: aws.String(queueName),
	})
	if err == nil {
		log.Printf("[INFO] SQS queue %q already exists\n", queueName)
		return nil
	}

	log.Printf("[INFO] Creating SQS queue %q...\n", queueName)
	_, err = client.CreateQueue(ctx, &sqs.CreateQueueInput{
		QueueName: aws.String(queueName),
	})
	if err != nil {
		return err
	}

	log.Printf("[INFO] SQS queue %q created successfully\n", queueName)
	return nil
}

func ensureTable(ctx context.Context, client *dynamodb.Client, tableName string) error {
	_, err := client.DescribeTable(ctx, &dynamodb.DescribeTableInput{
		TableName: aws.String(tableName),
	})
	if err == nil {
		log.Printf("[INFO] DynamoDB table %q already exists\n", tableName)
		return nil
	}

	log.Printf("[INFO] Creating DynamoDB table %q...\n", tableName)
	_, err = client.CreateTable(ctx, &dynamodb.CreateTableInput{
		TableName: aws.String(tableName),
		KeySchema: []dbtypes.KeySchemaElement{
			{
				AttributeName: aws.String("PK"),
				KeyType:       dbtypes.KeyTypeHash,
			},
		},
		AttributeDefinitions: []dbtypes.AttributeDefinition{
			{
				AttributeName: aws.String("PK"),
				AttributeType: dbtypes.ScalarAttributeTypeS,
			},
		},
		BillingMode: dbtypes.BillingModePayPerRequest,
	})
	if err != nil {
		return err
	}

	log.Printf("[INFO] DynamoDB table %q created successfully\n", tableName)
	return nil
}
