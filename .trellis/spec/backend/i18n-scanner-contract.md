# I18n Razor boundary contract

The scanner counts HTML text and title/placeholder/alt/value attributes after
removing expressions, pure code blocks, comments and script/style. Markup inside
a code block or expression remains excluded; a boundary repair must not enlarge
this scope or silently consume following display text.

Use Roslyn for one C# token at a time, including string and trivia semantics.
At Razor transitions, use dedicated markup matching: @<tag>, nested same-name
tags, self-closing/void tags, attribute expressions, @: lines, recursive Razor
expressions and direct HTML at a code statement boundary. Pure C# lexing cannot
interpret Razor template delegates. Uncertain/truncated boundaries fail closed
with I18N415, without source values. Each call owns its source and stacks; no
source cache. Mixed recursion is bounded at 96, stacks at 512.

Keep the original tests plus RazorLexicalBoundaryTests. Verify the actual repo
with zero I18N415. Compare old/new counts on identical tracked sources and report
every changed file. Baseline increases require the user's explicit allowed cause;
never update a baseline merely to hide a parser regression.
