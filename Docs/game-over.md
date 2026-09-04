# Game over

Three ways the player's campaign ends, and no fourth. They were settled by the user on
2026-09-04 and nothing may be added to the list without him.

| # | The end | What has to be true | The headline |
|---|---|---|---|
| 1 | The Don is shot | He is dead **and no lieutenant was left** to take the chair | THE DON IS DEAD |
| 2 | The Don is sentenced | He is convicted and it is a **life** sentence | THE DON GOES DOWN |
| 3 | The money runs out | **Three nights running** with an empty safe and not one man paid | THE BOOKS ARE CLOSED |

A raid on headquarters, a lost hood, a lost street and an empty turf map end nothing.
Victory - the last house standing - is not defined yet.

## What each one actually measures

**The Don is shot.** Death reaches the roster by one path, and succession happens
inside it: the most loyal lieutenant still on his feet takes the chair the moment the
Don is struck off, and the family plays on under a new man. So one bullet is not the
end; the end is a Don shot with the whole command already gone. This is the only one of
the three that also finishes a **rival** house.

**The Don is sentenced.** A term he can serve is not the end - his heir runs the family
and the discharge pass gives him back on the day the court named. Only life closes the
books. The test is the sentence, not the cell: a Don on remand, on bail or waiting on a
court day is a Don whose family is still trading.

**The money runs out.** A *broke night* is a midnight that closed with nothing in the
safe and not a single envelope handed out. Three of those in a row and the outfit is
finished. Any night that puts money in a man's hand resets the count to nought.

Three is deliberately the same number a hood deserts after, so the end arrives on the
night the outfit would start walking out anyway rather than a week into a corpse that
still had a cursor. It also means a safe that can pay even one man keeps the campaign
alive: the lieutenants are paid first, so an outfit down to its last few dollars still
buys itself nights.

## What the player sees

One black leaf over the dead city, at sorting order 400 - over the street, the plate,
the book and the map. It cannot be waved away, because there is no running game behind
it. It carries the day, the headline, the line that names what happened, and the tally
the outfit ended on: men left on the books and dollars in the safe.

## Where it lives

| what | where |
|---|---|
| the three endings and their words | `Assets/Scripts/Outfit/OutfitEnding.cs` |
| the one gate that decides | `CampaignRunner.CampaignOver` |
| the broke-night counter | `CampaignRunner.BrokeNights`, counted in `TurnTheBooks` |
| succession, so a dead Don is not automatically the end | `RosterOps.SucceedTheBoss` |
| what finishes a **rival** house instead | `House.Finished` (extinct or headless) |
| the black leaf | `Assets/RoadDemo/OutfitEnd.cs` |
| the contracts | `CommandTests`: `TheDonsDeathEndsIt`, `ALifeSentenceEndsIt`, `AServableTermDoesNot`, `ThreeBrokeNightsCloseTheBooks`, `ARivalHouseIsNotWoundUpByABadFortnight` |

The end is **not saved**. `CampaignSave` has never carried `Fallen`, and it does not
carry the broke-night count either, so a campaign loaded from a file starts its count
again. Worth closing when the save file next grows.
