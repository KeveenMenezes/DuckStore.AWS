package main

import (
	"context"
	"fmt"
	"log"

	"github.com/aws/aws-sdk-go-v2/feature/dynamodb/attributevalue"
	"github.com/aws/aws-sdk-go-v2/service/dynamodb"
	"github.com/aws/aws-sdk-go-v2/service/dynamodb/types"
)

// Store handles DynamoDB persistence for notification events.
type Store struct {
	client    *dynamodb.Client
	tableName string
}

// NewStore creates a new DynamoDB store.
func NewStore(client *dynamodb.Client, tableName string) *Store {
	return &Store{
		client:    client,
		tableName: tableName,
	}
}

// Marshal converts a NotificationEvent into a DynamoDB attribute map.
func (s *Store) Marshal(event NotificationEvent) (map[string]types.AttributeValue, error) {
	item, err := attributevalue.MarshalMap(event)
	if err != nil {
		return nil, fmt.Errorf("failed to marshal notification event: %w", err)
	}
	return item, nil
}

// Save persists a NotificationEvent to DynamoDB (used as a fallback when no event ID is available).
func (s *Store) Save(ctx context.Context, event NotificationEvent) error {
	item, err := s.Marshal(event)
	if err != nil {
		return err
	}

	_, err = s.client.PutItem(ctx, &dynamodb.PutItemInput{
		TableName: &s.tableName,
		Item:      item,
	})
	if err != nil {
		log.Printf("[ERROR] DynamoDB PutItem failed for ID=%s: %v\n", event.ID, err)
		return fmt.Errorf("failed to put item in DynamoDB: %w", err)
	}

	log.Printf("[INFO] DynamoDB PutItem succeeded for ID=%s, Table=%s\n", event.ID, s.tableName)
	return nil
}
