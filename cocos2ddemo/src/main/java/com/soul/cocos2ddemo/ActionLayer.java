package com.soul.cocos2ddemo;

import org.cocos2d.actions.base.CCAction;
import org.cocos2d.actions.base.CCRepeatForever;
import org.cocos2d.actions.ease.CCEaseBounceInOut;
import org.cocos2d.actions.ease.CCEaseIn;
import org.cocos2d.actions.ease.CCEaseInOut;
import org.cocos2d.actions.ease.CCEaseOut;
import org.cocos2d.actions.instant.CCCallFunc;
import org.cocos2d.actions.instant.CCHide;
import org.cocos2d.actions.instant.CCShow;
import org.cocos2d.actions.interval.CCBezierBy;
import org.cocos2d.actions.interval.CCBezierTo;
import org.cocos2d.actions.interval.CCBlink;
import org.cocos2d.actions.interval.CCDelayTime;
import org.cocos2d.actions.interval.CCFadeIn;
import org.cocos2d.actions.interval.CCFadeOut;
import org.cocos2d.actions.interval.CCFadeTo;
import org.cocos2d.actions.interval.CCJumpBy;
import org.cocos2d.actions.interval.CCJumpTo;
import org.cocos2d.actions.interval.CCMoveBy;
import org.cocos2d.actions.interval.CCMoveTo;
import org.cocos2d.actions.interval.CCRotateBy;
import org.cocos2d.actions.interval.CCRotateTo;
import org.cocos2d.actions.interval.CCScaleBy;
import org.cocos2d.actions.interval.CCScaleTo;
import org.cocos2d.actions.interval.CCSequence;
import org.cocos2d.actions.interval.CCSpawn;
import org.cocos2d.actions.interval.CCTintBy;
import org.cocos2d.layers.CCLayer;
import org.cocos2d.nodes.CCDirector;
import org.cocos2d.nodes.CCLabel;
import org.cocos2d.nodes.CCSprite;
import org.cocos2d.types.CCBezierConfig;
import org.cocos2d.types.CGSize;

/**
 * * @author soul
 *
 * @项目名:Game
 * @包名: com.soul.cocos2ddemo
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/3/14 22:50
 */

public class ActionLayer extends CCLayer {


    public ActionLayer() {

        //        ccmoveby(); //移动
        //        ccmoveto();
        //        ccscaleby();// 方法
        //        ccscaleto();

        //        ccrotateby();//旋转
        //        ccrotateto();

        //        ccjumpby();//跳跃
        //        ccjumpto();

        //        ccbezierby();//贝塞尔曲线
        //        ccbezierto();

        //        ccfadeto();//透明度
        //        ccfadein();
        //        ccfadeout();

        //        easein();//动作执行的速度
        //        easeout();
        //        easeinout();
        //        cceasebonceinout();

        //        tintby(); //着色

//        blink();//闪烁

        //并行动作
//        zuhe();

        //延迟时间
        ccdelaytime();

    }

    private void ccdelaytime() {
        //动作1
        CCDelayTime ccDelayTime = CCDelayTime.action(2);//延迟2s
        //动作2
        CCHide ccHide = CCHide.action();//隐藏
        //动作3
        CCShow ccShow = CCShow.action();//显示

        //动作4
        CCCallFunc saygoodBye = CCCallFunc.action(this, "saygoodBye");

        //串行动作
        CCSequence ccSequence = CCSequence.actions(ccDelayTime, ccHide,ccShow,saygoodBye);//2s隐藏精灵
        CCSprite zombie = getZombie();
        zombie.runAction(ccSequence);


    }

    public void saygoodBye(){
          System.out.println("今天到处为止");
    }

    private void zuhe() {
        //1 动作1
        CCBlink ccBlink = CCBlink.action(2, 5);
        //2 动作2
        CCMoveBy ccMoveBy = CCMoveBy.action(2, ccp(100, 100));
        CCSpawn ccSpawn = CCSpawn.actions(ccBlink, ccMoveBy);
        CCSprite zombie = getZombie();
        zombie.runAction(ccSpawn);


    }

    private void blink() {
            //1 参数1 ：持续时间。参数2 闪烁次数
        CCBlink action = CCBlink.action(2, 5);
        CCSprite zombie = getZombie();
        zombie.runAction(action);

    }

    /**
     * 着色
     * 把颜色结合在一起
     */
    private void tintby() {
        //CC3 这个方法也是在CCNode里面定义
        CCTintBy ccTintBy = CCTintBy.action(2, ccc3(250, 0, 0));

        //string :label显示的文本，fontname：字体  fontsize：字号
        CCLabel ccLabel = CCLabel.labelWithString("强大", "hkbd.ttf", 40);
        ccLabel.setColor(ccc3(50, 152, 203));

        //让cclabel居中显示
        CCDirector sharedDirector = CCDirector.sharedDirector();
        CGSize winSize = sharedDirector.getWinSize();
        float screenWidth = winSize.getWidth();
        float screenHeight = winSize.getHeight();
        //居中显示
        ccLabel.setPosition(screenWidth / 2, screenHeight / 2);

        //动作方向执行---》所有以by结尾的冬瓜子都可以反向执行
        CCTintBy ccTintByReverse = ccTintBy.reverse();
        //把两个动作串行执行
        CCSequence ccSequence = CCSequence.actions(ccTintBy, ccTintByReverse);

        //让一个动作重复执行
        CCRepeatForever ccRepeatForever = CCRepeatForever.action(ccSequence);
        //运行串行动作 永久执行
        ccLabel.runAction(ccRepeatForever);

        this.addChild(ccLabel);


    }

    private void cceasebonceinout() {

        CCMoveBy ccMoveBy = CCMoveBy.action(2, ccp(200, 200));

        CCEaseBounceInOut action = CCEaseBounceInOut.action(ccMoveBy);

        CCSprite zombie = getZombie();
        zombie.runAction(action);

    }

    /**
     * 慢--》快 --》慢
     */
    private void easeinout() {
        CCMoveBy ccMoveBy = CCMoveBy.action(2, ccp(200, 200));
        CCEaseInOut action = CCEaseInOut.action(ccMoveBy, 5);
        CCSprite zombie = getZombie();
        zombie.runAction(action);


    }

    /**
     * 让一个动作执行的时候，越来越慢
     */
    private void easeout() {
        CCMoveBy ccMoveBy = CCMoveBy.action(2, ccp(200, 200));
        CCEaseOut ccEaseIn = CCEaseOut.action(ccMoveBy, 5);//对一个动作进行包装
        CCSprite zombie = getZombie();
        zombie.runAction(ccEaseIn);
    }

    /**
     * 让一个动作执行的时候，越来越快
     */
    private void easein() {
        CCMoveBy ccMoveBy = CCMoveBy.action(2, ccp(200, 200));
        CCEaseIn ccEaseIn = CCEaseIn.action(ccMoveBy, 5);
        CCSprite zombie = getZombie();
        zombie.runAction(ccEaseIn);


    }

    private void ccfadeout() {

        CCFadeOut ccFadeIn = CCFadeOut.action(2);//2 秒 隐藏起来
        CCSprite zombie = getZombie();
        zombie.runAction(ccFadeIn);
    }

    private void ccfadein() {

        CCFadeIn ccFadeIn = CCFadeIn.action(2);//2 秒显示出来
        CCSprite zombie = getZombie();
        zombie.runAction(ccFadeIn);

    }

    private void ccfadeto() {
        //  参数1 ： 持续时间  参数2 ：透明度(0-255)
        CCFadeTo ccFadeTo = CCFadeTo.action(2, 100);
        CCSprite zombie = getZombie();
        zombie.runAction(ccFadeTo);
    }

    private void ccbezierto() {
        CCBezierConfig ccBezierConfig = new CCBezierConfig();
        ccBezierConfig.controlPoint_1 = ccp(50, 0);
        ccBezierConfig.controlPoint_2 = ccp(100, 150);
        ccBezierConfig.endPosition = ccp(250, 0);
        CCBezierTo ccBezierBy = CCBezierTo.action(2, ccBezierConfig);

        CCSprite heart = getHeart();
        heart.runAction(ccBezierBy);
    }

    private void ccbezierby() {
        CCBezierConfig ccBezierConfig = new CCBezierConfig();
        ccBezierConfig.controlPoint_1 = ccp(50, 0);
        ccBezierConfig.controlPoint_2 = ccp(100, 150);
        ccBezierConfig.endPosition = ccp(250, 0);
        CCBezierBy ccBezierBy = CCBezierBy.action(2, ccBezierConfig);

        CCSprite heart = getHeart();
        heart.runAction(ccBezierBy);


    }

    private void ccjumpto() {
        CCJumpTo ccJumpTo = CCJumpTo.action(2, ccp(100, 10), 100, 5);
        CCSprite zombie = getZombie();
        zombie.setPosition(50, 50);
        zombie.runAction(ccJumpTo);

    }

    private void ccjumpby() {
        //time:持续时间 pos 执行的参考点 height : 跳跃的高度 jumps 跳跃的词素
        CCAction ccJumpBy = CCJumpBy.action(2, ccp(100, 100), 100, 5);
        CCSprite zombie = getZombie();
        zombie.setPosition(50, 50);
        zombie.runAction(ccJumpBy);


    }

    private void ccrotateto() {
        CCRotateTo ccRotateTo = CCRotateTo.action(2, 270);
        CCSprite heart = getHeart();
        heart.setPosition(100, 100);
        heart.runAction(ccRotateTo);

    }

    private void ccrotateby() {
        //t 持续时间,a 旋转角度
        CCRotateBy ccRotateBy = CCRotateBy.action(2, 270);
        CCSprite heart = getHeart();
        heart.setPosition(100, 100);
        heart.runAction(ccRotateBy);


    }

    private void ccscaleto() {
        CCScaleTo ccScaleTo = CCScaleTo.action(2, 2);
        CCSprite heart = getHeart();
        heart.setScale(2);
        heart.runAction(ccScaleTo);


    }

    private void ccscaleby() {
        //1 创建动作 t 动作持续时间， s放大的倍速

        CCScaleBy ccScaleBy = CCScaleBy.action(2, 2);
        //2 创建精灵
        CCSprite heart = getHeart();
        heart.setScale(2);
        //3 精灵运行动作
        heart.runAction(ccScaleBy);


    }

    private void ccmoveto() {
        //1 创建动作 t 动作执行市场， pos 移动到那一点
        //ccp(100,100) 是在CCNode中定义的方法，CCNode是CCLayer父类
        CCAction action = CCMoveTo.action(2, ccp(100, 100));
        //2 创建精灵
        CCSprite zombie = getZombie();
        zombie.setPosition(50, 50);

        zombie.runAction(action);


    }

    private void ccmoveby() {

        //1 创建动作

        CCMoveBy ccMoveBy = CCMoveBy.action(2, ccp(100, 100));
        //2 创建精灵
        CCSprite zombie = getZombie();
        zombie.setPosition(50, 50);
        //3 精灵运行动作
        zombie.runAction(ccMoveBy);

    }

    /**
     * 返回一个zombie
     *
     * @return
     */
    public CCSprite getZombie() {
        CCSprite zombie = CCSprite.sprite("z_1_01.png");
        zombie.setAnchorPoint(0, 0);
        this.addChild(zombie);
        return zombie;
    }

    /**
     * 得到一个heart精灵
     *
     * @return
     */
    public CCSprite getHeart() {
        CCSprite heart = CCSprite.sprite("heart.png");
        heart.setAnchorPoint(0, 0);
        this.addChild(heart);
        return heart;
    }


}
