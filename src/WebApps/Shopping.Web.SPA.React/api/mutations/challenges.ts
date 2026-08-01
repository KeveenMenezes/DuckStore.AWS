// Lambda resolver, Cognito required (ADR-0045 §3, §4, §8) — grading needs the stored answer key
// and finalizes through a multi-item transaction, both beyond a direct resolver.
export const SUBMIT_CHALLENGE_ANSWER = `
  mutation SubmitChallengeAnswer($challengeId: ID!, $selectedOption: Int!) {
    submitChallengeAnswer(challengeId: $challengeId, selectedOption: $selectedOption) {
      isCorrect
      pointsEarned
      newScore
      explanation
      selectedOption
    }
  }
`

// Lambda resolver, Cognito required (ADR-0045 §6, §8) — the hint penalty is a conditional write
// that must commit before the hint text is returned.
export const REVEAL_CHALLENGE_HINT = `
  mutation RevealChallengeHint($challengeId: ID!) {
    revealChallengeHint(challengeId: $challengeId) {
      hint
      hintsRevealed
      penaltyApplied
    }
  }
`
