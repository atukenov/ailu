using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

// Deleting an account (privacy doc, "one of the two asks for full deletion"): the person's own data and
// their own records go; what the couple built together stays with the partner, without pointing to them.
//
// Gone: phone, name, sign-in sessions, devices, push history, moods, invitations, their memories with
// the photo files, their reactions, their answers to daily questions and to the love-languages quiz, goals they proposed that the
// partner never joined.
// Kept, anonymous: the couple's нұр ledger, phase and streak, the days of tasks done together, goals
// both had joined, the partner's own memories and answers. The user row stays as a bare id (no phone,
// no name) so those records keep a valid author, and the partner sees them unsigned.
// A current couple ends, as if the person had ended it: the partner can pair again.
// Subscription rows stay: they hold no personal data (a store transaction id, product, dates), the tax
// records need them, and the store keeps billing until the person cancels there, which deleting the
// account can't do. The app's confirmation says so.
// Gift orders and code redemptions stay for the same reason (payments, and a code entered once per
// person). A gift they bought and nobody has entered yet keeps working: it belongs to whoever got it.
public sealed class AccountDeletion(AiluDb db, IPhotoStore store, TimeProvider clock, ILogger<AccountDeletion> log)
{
    public async Task DeleteAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (user.DeletedAt is not null) return;
        var phone = user.Phone;

        List<(Guid CoupleId, Guid PhotoId)> files;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var memberships = await db.CoupleMembers.Include(m => m.Couple!).ThenInclude(c => c.Members)
                .Where(m => m.UserId == userId).ToListAsync(ct);
            foreach (var m in memberships.Where(m => m.LeftAt is null && m.Couple!.Status != CoupleStatus.Ended))
            {
                m.Couple!.Status = CoupleStatus.Ended;
                m.Couple.EndedAt = now;
                foreach (var member in m.Couple.Members.Where(x => x.LeftAt is null)) member.LeftAt = now;
            }

            // Memories: photos still counted against a couple's quota are given back first.
            var memories = await db.Memories.Include(m => m.Photos).Where(m => m.AuthorId == userId).ToListAsync(ct);
            foreach (var group in memories.Where(m => m.DeletedAt is null).GroupBy(m => m.CoupleId))
            {
                var photos = group.SelectMany(m => m.Photos).ToList();
                if (photos.Count == 0) continue;
                var bytes = photos.Sum(p => p.Bytes);
                await db.Couples.Where(c => c.Id == group.Key).ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.StorageBytes, c => c.StorageBytes - bytes)
                    .SetProperty(c => c.PhotoCount, c => c.PhotoCount - photos.Count), ct);
            }
            files = memories.SelectMany(m => m.Photos).Select(p => (p.CoupleId, p.Id)).ToList();
            db.Memories.RemoveRange(memories);

            await db.MemoryReactions.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
            await db.Moods.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.LoveQuizAnswers.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.MoodMonths.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.Devices.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.RefreshTokens.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.PushLog.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.Invitations.Where(i => i.InviterUserId == userId || i.Phone == phone).ExecuteDeleteAsync(ct);
            if (phone is not null) await db.PhoneCodes.Where(c => c.Phone == phone).ExecuteDeleteAsync(ct);

            // The answer text is theirs; when it was given (and the нұр for it) stays with the couple.
            foreach (var m in memberships)
            {
                if (m.Role == MemberRole.A)
                    await db.DailyQuestions.Where(q => q.CoupleId == m.CoupleId && q.AnswerA != null)
                        .ExecuteUpdateAsync(s => s.SetProperty(q => q.AnswerA, (string?)null), ct);
                else
                    await db.DailyQuestions.Where(q => q.CoupleId == m.CoupleId && q.AnswerB != null)
                        .ExecuteUpdateAsync(s => s.SetProperty(q => q.AnswerB, (string?)null), ct);
            }

            await db.Goals.Where(g => g.CreatedById == userId && g.JoinedAt == null && g.RemovedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.RemovedAt, now), ct);
            // A mark the partner never confirmed goes with them; earned achievements stay the couple's.
            await db.CoupleAchievements.Where(a => a.MarkedById == userId && a.EarnedAt == null).ExecuteDeleteAsync(ct);

            user.Phone = null;
            user.Name = null;
            user.MoodConsentAt = null;
            user.DeletedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // Files go after the rows, so a failure here leaves unreachable files, never rows without files.
        foreach (var (coupleId, photoId) in files)
        {
            try
            {
                await store.DeleteFolderAsync(PhotoKeys.Folder(coupleId, photoId), CancellationToken.None);
            }
            catch (Exception e)
            {
                log.LogError(e, "Could not delete photo {PhotoId} of a deleted account", photoId);
            }
        }
        log.LogInformation("Deleted account {UserId}", userId);
    }
}
