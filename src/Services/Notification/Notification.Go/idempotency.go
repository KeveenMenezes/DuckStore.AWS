package main

import (
	"context"
	"errors"
	"fmt"
	"log"
	"time"

	"github.com/aws/aws-sdk-go-v2/aws"
	"github.com/aws/aws-sdk-go-v2/service/dynamodb"
	"github.com/aws/aws-sdk-go-v2/service/dynamodb/types"
)

// IdempotentConsumer atomically registers an event ID in the processed-events table and
// executes the caller's business writes in a single TransactWriteItems. If the event ID
// was already registered, Consume returns nil without re-executing the business writes.
type IdempotentConsumer struct {
	client               *dynamodb.Client
	processedEventsTable string
}

func NewIdempotentConsumer(client *dynamodb.Client, processedEventsTable string) *IdempotentConsumer {
	return &IdempotentConsumer{
		client:               client,
		processedEventsTable: processedEventsTable,
	}
}

func (ic *IdempotentConsumer) Consume(ctx context.Context, eventID string, businessItems []types.TransactWriteItem) error {
	processedEventPut := types.TransactWriteItem{
		Put: &types.Put{
			TableName: aws.String(ic.processedEventsTable),
			Item: map[string]types.AttributeValue{
				"PK":             &types.AttributeValueMemberS{Value: eventID},
				"ProcessedOnUtc": &types.AttributeValueMemberS{Value: time.Now().UTC().Format(time.RFC3339)},
			},
			ConditionExpression: aws.String("attribute_not_exists(PK)"),
		},
	}

	items := append([]types.TransactWriteItem{processedEventPut}, businessItems...)

	_, err := ic.client.TransactWriteItems(ctx, &dynamodb.TransactWriteItemsInput{
		TransactItems: items,
	})
	if err != nil {
		var txCanceled *types.TransactionCanceledException
		if errors.As(err, &txCanceled) &&
			len(txCanceled.CancellationReasons) > 0 &&
			aws.ToString(txCanceled.CancellationReasons[0].Code) == "ConditionalCheckFailed" {
			log.Printf("[INFO] Event %s already processed — skipping\n", eventID)
			return nil
		}
		return fmt.Errorf("transact write failed for event %s: %w", eventID, err)
	}

	return nil
}
