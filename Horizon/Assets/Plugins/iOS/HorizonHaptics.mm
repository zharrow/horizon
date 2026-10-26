// Retours haptiques iOS (Taptic Engine), appelés depuis Haptics.cs.
// kind : 0 léger, 1 moyen, 2 fort, 3 succès, 4 avertissement.
#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator *impacts[3];
static UINotificationFeedbackGenerator *notifier;

extern "C" void HorizonHaptic(int kind)
{
    if (kind >= 0 && kind <= 2)
    {
        if (impacts[kind] == nil)
        {
            UIImpactFeedbackStyle style = kind == 0 ? UIImpactFeedbackStyleLight : kind == 1 ? UIImpactFeedbackStyleMedium : UIImpactFeedbackStyleHeavy;
            impacts[kind] = [[UIImpactFeedbackGenerator alloc] initWithStyle:style];
        }
        [impacts[kind] impactOccurred];
        [impacts[kind] prepare];
        return;
    }
    if (notifier == nil) notifier = [[UINotificationFeedbackGenerator alloc] init];
    [notifier notificationOccurred:kind == 3 ? UINotificationFeedbackTypeSuccess : UINotificationFeedbackTypeWarning];
}
