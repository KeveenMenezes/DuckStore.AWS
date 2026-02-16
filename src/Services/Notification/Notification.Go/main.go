package main

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/aws/aws-lambda-go/events"
	"github.com/aws/aws-lambda-go/lambda"
	"github.com/aws/aws-sdk-go-v2/aws"
	"github.com/aws/aws-sdk-go-v2/config"
	"github.com/aws/aws-sdk-go-v2/credentials"
	"github.com/aws/aws-sdk-go-v2/service/dynamodb"
	"github.com/aws/aws-sdk-go-v2/service/sqs"
)

const (
	defaultQueueName = "duckstore-notifications"
	defaultTableName = "duckstore-notifications"
	defaultRegion    = "us-east-1"
)

func main() {
	mode := os.Getenv("APP_MODE")

	switch mode {
	case "lambda":
		log.Println("[INFO] Starting in Lambda mode")
		lambda.Start(lambdaHandler)

	default:
		log.Println("[INFO] Starting in Aspire/standalone SQS poller mode")
		runPoller()
	}
}

// lambdaHandler processes SQS events when running as an AWS Lambda function.
func lambdaHandler(ctx context.Context, sqsEvent events.SQSEvent) error {
	log.Printf("[INFO] Lambda invoked with %d record(s)\n", len(sqsEvent.Records))

	cfg, err := newAWSConfig(ctx)
	if err != nil {
		return fmt.Errorf("failed to load AWS config: %w", err)
	}

	store := NewStore(dynamodb.NewFromConfig(cfg), getEnvOrDefault("DYNAMODB_TABLE", defaultTableName))

	for _, record := range sqsEvent.Records {
		if err := processMessage(ctx, store, record.Body); err != nil {
			log.Printf("[ERROR] Failed to process record %s: %v\n", record.MessageId, err)
			return err
		}
	}

	return nil
}

// runPoller continuously polls SQS for messages (standalone / Aspire mode).
func runPoller() {
	ctx, cancel := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	defer cancel()

	cfg, err := newAWSConfig(ctx)
	if err != nil {
		log.Fatalf("[FATAL] Failed to load AWS config: %v", err)
	}

	sqsClient := sqs.NewFromConfig(cfg)
	dynamoClient := dynamodb.NewFromConfig(cfg)

	tableName := getEnvOrDefault("DYNAMODB_TABLE", defaultTableName)
	store := NewStore(dynamoClient, tableName)

	// Resolve queue URL: prefer SQS_QUEUE_URL (injected by Aspire/CloudFormation),
	// fall back to looking up by SQS_QUEUE_NAME.
	queueURL := os.Getenv("SQS_QUEUE_URL")
	if queueURL == "" {
		queueName := getEnvOrDefault("SQS_QUEUE_NAME", defaultQueueName)

		// Ensure resources exist (only for local dev with custom endpoints like LocalStack)
		if os.Getenv("AWS_ENDPOINT_URL") != "" {
			if err := ensureResources(ctx, sqsClient, dynamoClient, queueName, tableName); err != nil {
				log.Fatalf("[FATAL] Failed to ensure AWS resources: %v", err)
			}
		}

		resolved, err := getQueueURL(ctx, sqsClient, queueName)
		if err != nil {
			log.Fatalf("[FATAL] Failed to get queue URL for %q: %v", queueName, err)
		}
		queueURL = resolved
	}

	log.Printf("[INFO] Polling SQS queue: %s\n", queueURL)
	log.Printf("[INFO] Saving to DynamoDB table: %s\n", tableName)

	for {
		select {
		case <-ctx.Done():
			log.Println("[INFO] Shutting down poller...")
			return
		default:
			poll(ctx, sqsClient, store, queueURL)
		}
	}
}

// poll receives messages from SQS and processes them.
func poll(ctx context.Context, client *sqs.Client, store *Store, queueURL string) {
	result, err := client.ReceiveMessage(ctx, &sqs.ReceiveMessageInput{
		QueueUrl:            aws.String(queueURL),
		MaxNumberOfMessages: 10,
		WaitTimeSeconds:     20,
	})
	if err != nil {
		log.Printf("[ERROR] Failed to receive messages: %v\n", err)
		time.Sleep(5 * time.Second)
		return
	}

	if len(result.Messages) == 0 {
		return
	}

	log.Printf("[INFO] Received %d message(s) from SQS\n", len(result.Messages))

	for _, msg := range result.Messages {
		body := aws.ToString(msg.Body)
		msgID := aws.ToString(msg.MessageId)

		log.Printf("[INFO] Processing message ID: %s\n", msgID)

		if err := processMessage(ctx, store, body); err != nil {
			log.Printf("[ERROR] Failed to process message %s: %v\n", msgID, err)
			continue
		}

		// Delete message after successful processing
		_, err := client.DeleteMessage(ctx, &sqs.DeleteMessageInput{
			QueueUrl:      aws.String(queueURL),
			ReceiptHandle: msg.ReceiptHandle,
		})
		if err != nil {
			log.Printf("[ERROR] Failed to delete message %s: %v\n", msgID, err)
		}
	}
}

// processMessage parses and saves a single SQS message body.
func processMessage(ctx context.Context, store *Store, body string) error {
	log.Printf("[INFO] Message received: %s\n", body)

	var notification NotificationEvent
	if err := json.Unmarshal([]byte(body), &notification); err != nil {
		return fmt.Errorf("failed to unmarshal message: %w", err)
	}

	// Populate metadata if not set
	if notification.ID == "" {
		notification.ID = fmt.Sprintf("notif-%d", time.Now().UnixNano())
	}
	if notification.ProcessedAt == "" {
		notification.ProcessedAt = time.Now().UTC().Format(time.RFC3339)
	}
	if notification.Status == "" {
		notification.Status = "processed"
	}

	if err := store.Save(ctx, notification); err != nil {
		log.Printf("[ERROR] Failed to save to DynamoDB: %v\n", err)
		return err
	}

	log.Printf("[INFO] Successfully saved notification %s to DynamoDB\n", notification.ID)
	return nil
}

// newAWSConfig builds the AWS SDK config, using LocalStack endpoint if set.
func newAWSConfig(ctx context.Context) (aws.Config, error) {
	region := getEnvOrDefault("AWS_REGION", defaultRegion)

	opts := []func(*config.LoadOptions) error{
		config.WithRegion(region),
	}

	endpoint := os.Getenv("AWS_ENDPOINT_URL")
	if endpoint != "" {
		log.Printf("[INFO] Using custom AWS endpoint: %s\n", endpoint)
		opts = append(opts,
			config.WithBaseEndpoint(endpoint),
			config.WithCredentialsProvider(credentials.NewStaticCredentialsProvider("test", "test", "test")),
		)
	}

	return config.LoadDefaultConfig(ctx, opts...)
}

func getQueueURL(ctx context.Context, client *sqs.Client, queueName string) (string, error) {
	result, err := client.GetQueueUrl(ctx, &sqs.GetQueueUrlInput{
		QueueName: aws.String(queueName),
	})
	if err != nil {
		return "", err
	}
	return aws.ToString(result.QueueUrl), nil
}

func getEnvOrDefault(key, defaultValue string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return defaultValue
}
