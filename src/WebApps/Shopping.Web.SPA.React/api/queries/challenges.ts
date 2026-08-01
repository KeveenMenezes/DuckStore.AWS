// Public (API_KEY or Cognito) — direct DynamoDB resolver. GSI1 is sparse so the ANSWER item
// (the correct option's index, the explanation, the hint texts) can never surface here (ADR-0045 §2).
export const GET_CHALLENGES = `
  query GetChallenges($pageSize: Int, $nextToken: String) {
    challenges(pageSize: $pageSize, nextToken: $nextToken) {
      items {
        id
        title
        description
        code
        options
        difficulty
        language
        points
        hintCount
      }
      nextToken
    }
  }
`

// Cognito required — guests play but don't score (ADR-0045 §7).
export const GET_MY_CHALLENGE_PROGRESS = `
  query GetMyChallengeProgress {
    myChallengeProgress {
      score
      completed
      correctCount
      wrongCount
      hintsUsed
      currentStreak
      lastAnsweredAt
      byLanguage
      attempts {
        questionId
        isCorrect
        selectedOption
        hintsRevealed
        pointsEarned
        answeredAt
      }
    }
  }
`
